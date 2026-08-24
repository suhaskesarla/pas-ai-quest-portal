using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using System.IO.Compression;
using Microsoft.Extensions.Options;
using PAS.AIQuestPortal.Api.Configuration;
using PAS.AIQuestPortal.Api.Evidence;
using PAS.AIQuestPortal.Api.Workflow;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace PAS.AIQuestPortal.Api.Tests;

public sealed class EvidenceAttachmentTests
{
    [Fact]
    public void Filename_normalization_removes_paths_controls_and_uses_fallback()
    {
        Assert.Equal("proof.pdf", EvidenceAttachmentValidator.NormalizeFileName("C:\\fakepath\\pro\0of.pdf"));
        Assert.Equal("attachment", EvidenceAttachmentValidator.NormalizeFileName("../\0.."));
        Assert.True(EvidenceAttachmentValidator.NormalizeFileName(new string('x', 300) + ".txt").Length <= 255);
    }

    [Theory]
    [InlineData("detected", 422, "MalwareDetected")]
    [InlineData("failed", 503, "MalwareScanUnavailable")]
    public async Task Malware_results_fail_closed(string result, int status, string code)
    {
        var validator = Validator(new FixedScanner(result == "detected" ? MalwareScanResult.Detected : MalwareScanResult.Failed));
        WorkflowException error = await Assert.ThrowsAsync<WorkflowException>(() => validator.ValidateAsync([Upload("key", "Proof", "proof.png", "image/png", Png())], default));
        Assert.Equal(status, error.Status); Assert.Equal(code, error.Code);
    }

    [Fact]
    public async Task Signature_mismatch_and_disallowed_extension_are_rejected()
    {
        EvidenceAttachmentValidator validator = Validator(new FixedScanner(MalwareScanResult.Clean));
        WorkflowException mismatch = await Assert.ThrowsAsync<WorkflowException>(() => validator.ValidateAsync([Upload("a", "Proof", "proof.png", "image/png", "%PDF-"u8.ToArray())], default));
        Assert.Equal("AttachmentTypeMismatch", mismatch.Code);
        WorkflowException disallowed = await Assert.ThrowsAsync<WorkflowException>(() => validator.ValidateAsync([Upload("a", "Proof", "run.exe", "application/octet-stream", [0x4d, 0x5a])], default));
        Assert.Equal("AttachmentTypeNotAllowed", disallowed.Code);
    }

    [Fact]
    public async Task Macro_enabled_office_content_is_rejected()
    {
        EvidenceAttachmentValidator validator = Validator(new FixedScanner(MalwareScanResult.Clean));
        WorkflowException error = await Assert.ThrowsAsync<WorkflowException>(() => validator.ValidateAsync([Upload("a", "Proof", "proof.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", OfficeWithMacro())], default));
        Assert.Equal("AttachmentTypeMismatch", error.Code);
    }

    [Fact]
    public async Task Count_and_size_limits_are_enforced()
    {
        var options = new StorageOptions { BlobServiceUri = "http://localhost", Evidence = new() { MaxAttachmentsPerRequest = 1, MaxBytesPerFile = 8, MaxBytesPerRequest = 8 } };
        var validator = new EvidenceAttachmentValidator(Options.Create(options), new FixedScanner(MalwareScanResult.Clean));
        Assert.Equal("TooManyAttachments", (await Assert.ThrowsAsync<WorkflowException>(() => validator.ValidateAsync([Upload("a", "A", "a.png", "image/png", Png()), Upload("b", "B", "b.png", "image/png", Png())], default))).Code);
        Assert.Equal("AttachmentRequestTooLarge", (await Assert.ThrowsAsync<WorkflowException>(() => validator.ValidateAsync([Upload("a", "A", "a.png", "image/png", Png())], default))).Code);
    }

    [Fact]
    public async Task Azurite_store_uses_private_container_and_round_trips()
    {
        string connection = Environment.GetEnvironmentVariable("AZURITE_CONNECTION_STRING") ?? "UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://azurite";
        var service = new BlobServiceClient(connection); var options = Options.Create(new StorageOptions { BlobServiceUri = service.Uri.ToString(), ConnectionString = connection });
        var store = new AzureEvidenceBlobStore(service, options); await store.EnsurePrivateContainerAsync(default); string key = $"tests/{Guid.NewGuid():N}"; byte[] bytes = Png();
        await store.PutAsync(new(key, new MemoryStream(bytes), "image/png"), default);
        BlobContainerProperties properties = (await service.GetBlobContainerClient(AzureEvidenceBlobStore.ContainerName).GetPropertiesAsync()).Value;
        Assert.Equal(PublicAccessType.None, properties.PublicAccess);
        EvidenceBlobRead read = await store.OpenReadAsync(key, default); using var memory = new MemoryStream(); await read.Content.CopyToAsync(memory); Assert.Equal(bytes, memory.ToArray());
        await store.DeleteUncommittedAsync(key, default);
    }

    [Fact]
    public async Task Existing_public_container_is_corrected_to_private()
    {
        string connection=Environment.GetEnvironmentVariable("AZURITE_CONNECTION_STRING")??"UseDevelopmentStorage=true;DevelopmentStorageProxyUri=http://azurite";var service=new BlobServiceClient(connection);var container=service.GetBlobContainerClient(AzureEvidenceBlobStore.ContainerName);await container.CreateIfNotExistsAsync();await container.SetAccessPolicyAsync(PublicAccessType.Blob);
        var store=new AzureEvidenceBlobStore(service,Options.Create(new StorageOptions{BlobServiceUri=service.Uri.ToString(),ConnectionString=connection}));await store.EnsurePrivateContainerAsync(default);Assert.Equal(PublicAccessType.None,(await container.GetPropertiesAsync()).Value.PublicAccess);
    }

    [Fact]
    public void Configuration_forbids_production_shared_key_and_enabled_without_real_scanner()
    {
        var production=new StorageOptionsValidator(new Host("Production"));
        Assert.True(production.Validate(null,new StorageOptions{BlobServiceUri="https://example.blob.core.windows.net",ConnectionString="AccountName=x;AccountKey=y",Evidence=new(){Enabled=false}}).Failed);
        Assert.True(production.Validate(null,new StorageOptions{BlobServiceUri="https://example.blob.core.windows.net",Evidence=new(){Enabled=true}}).Failed);
        var development=new StorageOptionsValidator(new Host("Development"));Assert.True(development.Validate(null,new StorageOptions{BlobServiceUri="http://azurite",ConnectionString="UseDevelopmentStorage=true",Evidence=new(){Enabled=true,MalwareScanner="DeterministicPassThrough"}}).Succeeded);
    }

    [Fact]
    public async Task Disabled_attachments_fail_closed_instead_of_bypassing_scanner()
    {
        var validator=new EvidenceAttachmentValidator(Options.Create(new StorageOptions{BlobServiceUri="http://localhost",Evidence=new(){Enabled=false}}),new FixedScanner(MalwareScanResult.Clean));
        WorkflowException error=await Assert.ThrowsAsync<WorkflowException>(()=>validator.ValidateAsync([Upload("a","A","a.png","image/png",Png())],default));Assert.Equal("InvalidMultipartEvidence",error.Code);
    }

    [Theory]
    [InlineData("png")][InlineData("pdf")][InlineData("mp4")]
    public async Task Truncated_or_shallow_magic_files_are_rejected(string kind)
    {
        (string name,string mime,byte[] bytes)=kind switch{"png"=>("x.png","image/png",[0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a]),"pdf"=>("x.pdf","application/pdf","%PDF-1.7"u8.ToArray()),_=>("x.mp4","video/mp4",[0,0,0,16,0x66,0x74,0x79,0x70,0x69,0x73,0x6f,0x6d])};
        WorkflowException error=await Assert.ThrowsAsync<WorkflowException>(()=>Validator(new FixedScanner(MalwareScanResult.Clean)).ValidateAsync([Upload("a","A",name,mime,bytes)],default));Assert.Equal("AttachmentTypeMismatch",error.Code);
    }

    [Fact]
    public async Task Ooxml_high_compression_and_excessive_entries_are_rejected()
    {
        EvidenceAttachmentValidator validator=Validator(new FixedScanner(MalwareScanResult.Clean));
        foreach(byte[] package in new[]{OfficeBomb(),OfficeManyEntries()}){WorkflowException error=await Assert.ThrowsAsync<WorkflowException>(()=>validator.ValidateAsync([Upload("a","A","x.docx","application/vnd.openxmlformats-officedocument.wordprocessingml.document",package)],default));Assert.Equal("AttachmentTypeMismatch",error.Code);}
    }

    [Fact]
    public void Azure_read_access_is_five_minute_read_only_user_delegation_sas_shape()
    {
        DateTimeOffset starts = DateTimeOffset.UtcNow, expires = starts.AddMinutes(5);
        BlobSasBuilder sas = AzureEvidenceBlobStore.CreateReadSas("submissions/a/b", "application/pdf", "proof.pdf", starts, expires);
        Assert.Equal(AzureEvidenceBlobStore.ContainerName, sas.BlobContainerName); Assert.Equal("submissions/a/b", sas.BlobName);
        Assert.Equal("r", sas.Permissions); Assert.Equal(starts, sas.StartsOn); Assert.Equal(expires, sas.ExpiresOn);
        Assert.Equal("application/pdf", sas.ContentType); Assert.Contains("proof.pdf", sas.ContentDisposition);
    }

    private static EvidenceAttachmentValidator Validator(IEvidenceMalwareScanner scanner) => new(Options.Create(new StorageOptions { BlobServiceUri = "http://localhost" }), scanner);
    private static AttachmentUpload Upload(string key,string label,string name,string mime,byte[] bytes){string path=Path.GetTempFileName();File.WriteAllBytes(path,bytes);return new(key,label,name,mime,path,bytes.LongLength);}
    private static byte[] Png() => [0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a, 0,0,0,13, 0x49,0x48,0x44,0x52, 0,0,0,1,0,0,0,1,8,2,0,0,0, 0,0,0,0, 0,0,0,0,0x49,0x45,0x4e,0x44,0,0,0,0];
    private static byte[] OfficeWithMacro()
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry types = archive.CreateEntry("[Content_Types].xml"); using (var writer = new StreamWriter(types.Open())) writer.Write("application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml");
            archive.CreateEntry("word/document.xml"); archive.CreateEntry("word/vbaProject.bin");
        }
        return memory.ToArray();
    }
    private static byte[] OfficeBomb()=>OfficePackage(Enumerable.Repeat((byte)0,1024*1024).ToArray(),0);
    private static byte[] OfficeManyEntries()=>OfficePackage([1],257);
    private static byte[] OfficePackage(byte[] document,int extras){using var m=new MemoryStream();using(var z=new ZipArchive(m,ZipArchiveMode.Create,true)){ZipArchiveEntry t=z.CreateEntry("[Content_Types].xml");using(var w=new StreamWriter(t.Open()))w.Write("application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml");using(Stream d=z.CreateEntry("word/document.xml",CompressionLevel.SmallestSize).Open())d.Write(document);for(int i=0;i<extras;i++)z.CreateEntry($"word/x{i}.xml");}return m.ToArray();}
    private sealed class FixedScanner(MalwareScanResult result) : IEvidenceMalwareScanner { public Task<MalwareScanResult> ScanAsync(Stream content, CancellationToken cancellationToken) => Task.FromResult(result); }
    private sealed class Host(string name):IHostEnvironment{public string EnvironmentName{get;set;}=name;public string ApplicationName{get;set;}="Tests";public string ContentRootPath{get;set;}=".";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();}
}
