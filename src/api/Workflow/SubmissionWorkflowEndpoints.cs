using System.Text.Json.Serialization;
using System.Text.Json;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Authentication;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Data;
using PAS.AIQuestPortal.Api.Evidence;

namespace PAS.AIQuestPortal.Api.Workflow;

public static class SubmissionWorkflowEndpoints
{
    public static IServiceCollection AddSubmissionWorkflow(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<SubmissionWorkflowService>();
        services.Configure<JsonOptions>(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }

    public static void MapSubmissionWorkflow(this WebApplication app)
    {
        app.MapGet("/api/challenges/eligible", (SubmissionWorkflowService service, CancellationToken ct) => Run(() => service.EligibleAsync(ct))).RequireAuthorization(QuestPolicies.Participant);
        app.MapGet("/api/submissions/mine", (SubmissionWorkflowService service, CancellationToken ct) => Run(() => service.MineAsync(ct))).RequireAuthorization(QuestPolicies.Participant);
        app.MapPost("/api/submissions", (SubmissionWorkflowService service, IOptions<StorageOptions> options, HttpRequest request, CancellationToken ct) => Run(async () =>
        {
            (CreateSubmissionRequest payload, IReadOnlyList<AttachmentUpload> files) = await Parse<CreateSubmissionRequest>(request, options.Value, ct);
            try { return await service.CreateAsync(payload, files, ct); } finally { foreach (AttachmentUpload file in files) await file.DisposeAsync(); }
        })).RequireAuthorization(QuestPolicies.Participant);
        app.MapPut("/api/submissions/{id:guid}/resubmission", (SubmissionWorkflowService service, IOptions<StorageOptions> options, HttpRequest request, Guid id, CancellationToken ct) => Run(async () =>
        {
            (ResubmitRequest payload, IReadOnlyList<AttachmentUpload> files) = await Parse<ResubmitRequest>(request, options.Value, ct);
            try { return await service.ResubmitAsync(id, payload, files, ct); } finally { foreach (AttachmentUpload file in files) await file.DisposeAsync(); }
        })).RequireAuthorization(QuestPolicies.Participant);
        app.MapGet("/api/submissions/review-queue", (SubmissionWorkflowService service, CancellationToken ct) => Run(() => service.QueueAsync(ct))).RequireAuthorization(QuestPolicies.Manager);
        app.MapPost("/api/submissions/{id:guid}/review", (SubmissionWorkflowService service, Guid id, ReviewRequest request, CancellationToken ct) => Run(() => service.ReviewAsync(id, request, ct))).RequireAuthorization(QuestPolicies.Manager);
        app.MapPost("/api/manager/xp/{entryId:guid}/corrections", (SubmissionWorkflowService service, Guid entryId, CorrectionRequest request, CancellationToken ct) => Run(() => service.CorrectAsync(entryId, request, ct))).RequireAuthorization(QuestPolicies.Manager);
        app.MapGet("/api/submission-evidence/{evidenceId:guid}/content", async (SubmissionWorkflowService service, HttpContext http, Guid evidenceId, CancellationToken ct) =>
        {
            try
            {
                EvidenceReadAccess access = await service.EvidenceContentAsync(evidenceId, ct);
                http.Response.Headers.CacheControl = "private, no-store";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                if (access.RedirectUri is not null) return Results.Redirect(access.RedirectUri.ToString());
                EvidenceBlobRead content = access.Stream!;
                var disposition = new ContentDispositionHeaderValue("attachment") { FileName = "attachment", FileNameStar = EvidenceAttachmentValidator.NormalizeFileName(access.OriginalFileName) };
                http.Response.Headers.ContentDisposition = disposition.ToString();
                return Results.Stream(content.Content, content.MimeType, enableRangeProcessing: false);
            }
            catch (WorkflowException error) { return Problem(error); }
        }).RequireAuthorization(QuestPolicies.Authenticated);
    }

    private static readonly JsonSerializerOptions MultipartJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private sealed record RawFile(string Key,string Name,string Mime,string Path,long Size);
    private static async Task<(T Payload, IReadOnlyList<AttachmentUpload> Files)> Parse<T>(HttpRequest request, StorageOptions storage, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            T? json = await request.ReadFromJsonAsync<T>(MultipartJson, ct);
            return (json ?? throw new WorkflowException(400, "InvalidMultipartEvidence", "The request payload is required."), []);
        }
        if (!storage.Evidence.Enabled) throw new WorkflowException(400,"InvalidMultipartEvidence","Attachment evidence is disabled.");
        long requestLimit=storage.Evidence.MaxBytesPerRequest+1024*1024;IHttpMaxRequestBodySizeFeature? sizeFeature=request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();if(sizeFeature is { IsReadOnly:false })sizeFeature.MaxRequestBodySize=requestLimit;
        if(request.ContentLength>requestLimit)throw new WorkflowException(413,"AttachmentRequestTooLarge","The multipart request is too large.");
        string? boundary=Microsoft.Net.Http.Headers.HeaderUtilities.RemoveQuotes(Microsoft.Net.Http.Headers.MediaTypeHeaderValue.Parse(request.ContentType).Boundary).Value;if(string.IsNullOrWhiteSpace(boundary))throw new WorkflowException(400,"InvalidMultipartEvidence","The multipart boundary is missing.");
        var reader=new MultipartReader(boundary,request.Body){BodyLengthLimit=storage.Evidence.MaxBytesPerFile+1024*1024};var raw=new List<RawFile>();byte[]? payloadBytes=null;long combined=0;
        try
        {
            MultipartSection? section;while((section=await reader.ReadNextSectionAsync(ct))is not null)
            {
                if(!Microsoft.Net.Http.Headers.ContentDispositionHeaderValue.TryParse(section.ContentDisposition,out var disposition))throw new WorkflowException(400,"InvalidMultipartEvidence","A multipart part has invalid disposition.");
                string name=Microsoft.Net.Http.Headers.HeaderUtilities.RemoveQuotes(disposition.Name).Value??"";string? filename=Microsoft.Net.Http.Headers.HeaderUtilities.RemoveQuotes(disposition.FileNameStar).Value??Microsoft.Net.Http.Headers.HeaderUtilities.RemoveQuotes(disposition.FileName).Value;
                if(name=="payload")
                {
                    if(payloadBytes is not null||!string.Equals(section.ContentType?.Split(';',2)[0].Trim(),"application/json",StringComparison.OrdinalIgnoreCase))throw new WorkflowException(400,"InvalidMultipartEvidence","Exactly one application/json payload part is required.");
                    payloadBytes=await ReadBounded(section.Body,1024*1024,"InvalidMultipartEvidence",ct);continue;
                }
                if(filename is null)throw new WorkflowException(400,"InvalidMultipartEvidence","A non-payload multipart part must be an attachment file part.");
                if(raw.Count>=storage.Evidence.MaxAttachmentsPerRequest)throw new WorkflowException(413,"TooManyAttachments","Too many attachments.");
                if(raw.Any(x=>x.Key==name))throw new WorkflowException(400,"InvalidMultipartEvidence","File-part correlation keys must be unique.");
                string temp=Path.GetTempFileName();long fileSize=0;try{await using var output=new FileStream(temp,FileMode.Truncate,FileAccess.Write,FileShare.None,65536,FileOptions.Asynchronous|FileOptions.SequentialScan);byte[] buffer=new byte[65536];while(true){int n=await section.Body.ReadAsync(buffer,ct);if(n==0)break;fileSize+=n;combined+=n;if(fileSize>storage.Evidence.MaxBytesPerFile)throw new WorkflowException(413,"AttachmentTooLarge","An attachment is too large.");if(combined>storage.Evidence.MaxBytesPerRequest)throw new WorkflowException(413,"AttachmentRequestTooLarge","Combined attachment size is too large.");await output.WriteAsync(buffer.AsMemory(0,n),ct);}}catch{File.Delete(temp);throw;}raw.Add(new(name,filename,section.ContentType??"",temp,fileSize));
            }
            if(payloadBytes is null)throw new WorkflowException(400,"InvalidMultipartEvidence","The payload part is required.");T payload;try{payload=JsonSerializer.Deserialize<T>(payloadBytes,MultipartJson)??throw new JsonException();}catch(JsonException){throw new WorkflowException(400,"InvalidMultipartEvidence","The payload part is not valid JSON.");}

            IReadOnlyList<EvidenceItem> evidence=payload switch{CreateSubmissionRequest create=>create.Evidence,ResubmitRequest resubmit=>resubmit.Evidence,_=>throw new InvalidOperationException()};EvidenceItem[] descriptors=evidence.Where(x=>x.Kind==EvidenceKind.Attachment).ToArray();
            if(descriptors.Any(x=>string.IsNullOrWhiteSpace(x.FileKey))||descriptors.Select(x=>x.FileKey).Distinct(StringComparer.Ordinal).Count()!=descriptors.Length)throw new WorkflowException(400,"InvalidMultipartEvidence","Attachment fileKey values must be unique.");
            if(descriptors.Any(d=>raw.All(f=>f.Key!=d.FileKey)))throw new WorkflowException(400,"AttachmentFileMissing","An attachment file part is missing.");if(raw.Any(f=>descriptors.All(d=>d.FileKey!=f.Key)))throw new WorkflowException(400,"AttachmentFileUnexpected","An unexpected attachment file part was supplied.");
            return(payload,raw.Select(f=>new AttachmentUpload(f.Key,descriptors.Single(d=>d.FileKey==f.Key).Label,f.Name,f.Mime,f.Path,f.Size)).ToArray());
        }
        catch{foreach(RawFile file in raw)try{File.Delete(file.Path);}catch{}throw;}
    }

    private static async Task<byte[]> ReadBounded(Stream input,int max,string code,CancellationToken ct){using var output=new MemoryStream();byte[] b=new byte[8192];while(true){int n=await input.ReadAsync(b,ct);if(n==0)break;if(output.Length+n>max)throw new WorkflowException(413,code,"Multipart metadata is too large.");await output.WriteAsync(b.AsMemory(0,n),ct);}return output.ToArray();}

    private static async Task<IResult> Run<T>(Func<Task<T>> operation)
    {
        try { return Results.Ok(await operation()); }
        catch (WorkflowException error)
        {
            return Problem(error);
        }
    }

    private static IResult Problem(WorkflowException error) => Results.Problem(statusCode: error.Status, title: error.Code, detail: error.Message, extensions: new Dictionary<string, object?> { ["code"] = error.Code });
}
