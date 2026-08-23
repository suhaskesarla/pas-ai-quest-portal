import React, { useState, useMemo } from "react";
import {
  LayoutDashboard, Swords, UploadCloud, Users, Trophy, ClipboardCheck,
  BarChart3, Flame, Clock, CheckCircle2, XCircle, Hourglass, Sparkles,
  ChevronRight, ChevronDown, Plus, Image as ImageIcon, FileText, Video, ShieldCheck, History, Table, ListChecks,
  Wand2, Paperclip, X, Send, AlertTriangle, RotateCcw, MessageSquare,
} from "lucide-react";
import {
  BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer,
} from "recharts";

// ---------- design tokens, sourced from cpaaustralia.com ----------
const C = {
  bg: "#F4F5F7",
  surface: "#FFFFFF",
  surfaceMuted: "#F7F8FA",
  navy: "#0A1F44",
  navyLight: "#12305E",
  blue: "#1B5FCE",
  blueDim: "#E8EFFB",
  gold: "#FFC72C",
  goldDim: "#FCF1CE",
  purple: "#6A2C91",
  purpleDim: "#F1E7F6",
  teal: "#00A19A",
  tealDim: "#E1F5F3",
  orange: "#E8631C",
  orangeDim: "#FCE9DD",
  border: "#E2E4EA",
  borderStrong: "#C9CDD8",
  text: "#0A1F44",
  muted: "#5B6472",
  green: "#1E8E5A",
  greenDim: "#E4F4EB",
  red: "#C22C2C",
  redDim: "#FBE7E7",
};

const fontDisplay = "'Sora', 'Segoe UI', sans-serif";
const fontBody = "'Inter', 'Segoe UI', sans-serif";
const fontMono = "'JetBrains Mono', monospace";

const CATEGORY_COLOR = {
  "Friday Funny": { fg: C.purple, bg: C.purpleDim },
  "Go Pass": { fg: C.blue, bg: C.blueDim },
  "Raid": { fg: C.teal, bg: C.tealDim },
};
const categoryColor = (cat) => CATEGORY_COLOR[cat] || { fg: C.blue, bg: C.blueDim };

// ---------- mock data (frozen-spec-aligned UX/interaction shape only, not a real ledger) ----------
const CHARACTER_ROSTER_BY_CYCLE = {
  jul26: [{ name: "Master Prompt-Fu", role: "Guide" }],
  aug26: [
    { name: "Vega", role: "Challenger" },
    { name: "Aria", role: "Guide" },
    { name: "Lumen", role: "Guide" },
    { name: "Nova", role: "Guide" },
  ],
};

const CURRENT_USER = { name: "Suhas Kesarla" };

const CYCLES = [
  { id: "jul26", label: "July 2026", status: "closed" },
  { id: "aug26", label: "August 2026", status: "active" },
];
const CURRENT_CYCLE = "aug26";

// Award categories are data, not a hardcoded enum (spec §4) — Preety can add more without a deployment.
const AWARD_CATEGORIES = [
  { code: "EARLY_BIRD", label: "Early Bird Bonus" },
  { code: "BUDDY_ENROLMENT", label: "Buddy Enrolment Bonus" },
  { code: "RAID", label: "Raid Participation" },
  { code: "FRIDAY_FUNNY", label: "Friday Funny Bonus" },
  { code: "BIRTHDAY", label: "Birthday Shout-out" },
  { code: "OTHER", label: "Other" },
];
const categoryLabel = (code) => AWARD_CATEGORIES.find((c) => c.code === code)?.label || code;

// Explicit mock cycle roster, including zero-XP participants (spec §3 — CycleParticipant).
// This demonstrates that roster membership is not reconstructed from team/submission/award activity.
const CYCLE_ROSTER = {
  jul26: ["Suhas Kesarla", "Paul Gregg", "Saurabh Chaudhary", "Arun Vadlakonda", "Kavita Khanna", "Neil Yanlin", "Diane Clark", "Angela Kaur"],
  aug26: ["Suhas Kesarla", "Kanika Mehta", "Pooja Jhawar", "Kavita Khanna", "Saurabh Chaudhary", "Yanlin Gong", "Divya Varghese", "Keerthana Manogaran", "Yasir", "Diane Clark", "Angela Kaur", "Nikhil Gosavi"],
};

// Challenges: cycleId is reporting attribution (spec §2/§4's cycleId rule), status is what
// actually gates eligibility. A challenge can remain "open" past its cycle's calendar month —
// this is the core fix behind the whole three-lifecycle correction.
const initialChallenges = [
  {
    id: "c0",
    cycleId: "jul26",
    eyebrow: "GO PASS 03 · STILL OPEN",
    name: "Promptflix — Master Your AI Prompts",
    desc: "Build and document your top 5 AI prompts, in pairs or trios.",
    category: "Go Pass",
    due: "21 Aug (extended from July)",
    status: "open",
    tasks: [
      { id: "t0a", name: "Form a pair or trio and pick a movie theme", xp: 20, evidence: "Attachment", scoringMode: "individual" },
      { id: "t0b", name: "Submit your Top 5 AI Prompts document", xp: 40, evidence: "Attachment", scoringMode: "whole-team" },
      { id: "t0c", name: "Submit supporting artifact / video", xp: 30, evidence: "Attachment", scoringMode: "whole-team" },
    ],
  },
  {
    id: "c0b",
    cycleId: "jul26",
    eyebrow: "FRIDAY FUNNY · JULY",
    name: "Wheel of AI Fortune",
    desc: "Weekly funny AI-themed submission for the July Friday challenge.",
    category: "Friday Funny",
    due: "Closed",
    status: "closed",
    tasks: [
      { id: "t0d", name: "Post your Friday Funny entry", xp: 10, evidence: "Attachment", scoringMode: "individual" },
    ],
  },
  {
    id: "c1",
    cycleId: "aug26",
    eyebrow: "FRIDAY FUNNY · CLOSED",
    name: "Make Diane Laugh: The AI Team-Building Challenge",
    desc: "Use AI to invent a team-building activity for Diane. The funnier, the better.",
    category: "Friday Funny",
    due: "Closed",
    status: "closed",
    tasks: [
      { id: "t1", name: "Ask AI for a funny team-building activity", xp: 10, evidence: "Attachment", scoringMode: "individual" },
      { id: "t2", name: "Bonus: funniest idea of the week", xp: 5, evidence: "Attachment", scoringMode: "individual" },
      { id: "t3", name: "Bonus: most feasible idea we could run", xp: 5, evidence: "Attachment", scoringMode: "individual" },
    ],
  },
  {
    id: "c2",
    cycleId: "aug26",
    eyebrow: "AUGUST CHALLENGE · GO PASS 04",
    name: "Develop AI Agents on Azure",
    desc: "Build and register a working AI agent, then form or join a team for August.",
    category: "Go Pass",
    due: "31 Aug",
    status: "open",
    tasks: [
      { id: "t4", name: "Team formation — name, members, mission", xp: 10, evidence: "Multiple", scoringMode: "whole-team" },
      { id: "t5", name: "Complete the Azure AI agents learning path", xp: 20, evidence: "Attachment", scoringMode: "claimant-selects" },
      { id: "t6", name: "Submit your working agent + short writeup", xp: 30, evidence: "Multiple", scoringMode: "claimant-selects" },
    ],
  },
  {
    id: "c3",
    cycleId: "aug26",
    eyebrow: "RAID · GYM B",
    name: "AI Tools Raid",
    desc: "Live or remote raid focused on prompting and AI tools.",
    category: "Raid",
    due: "Closed",
    status: "closed",
    tasks: [
      { id: "t7", name: "Attend and answer raid questions", xp: 12, evidence: "None", scoringMode: "attendance" },
    ],
  },
];

const initialTeams = [
  { id: "jteam1", cycleId: "jul26", name: "Indiana Squad", members: ["Paul Gregg", "Saurabh Chaudhary"] },
  { id: "jteam2", cycleId: "jul26", name: "Prompt Pairs", members: ["Arun Vadlakonda", "Kavita Khanna", "Neil Yanlin"] },
  { id: "ateam1", cycleId: "aug26", name: "AI-Migos", members: ["Suhas Kesarla", "Kanika Mehta", "Pooja Jhawar"] },
  { id: "ateam2", cycleId: "aug26", name: "Bulls-AI", members: ["Kavita Khanna", "Saurabh Chaudhary", "Yanlin Gong"] },
  { id: "ateam3", cycleId: "aug26", name: "Threebotics", members: ["Divya Varghese", "Keerthana Manogaran", "Yasir"] },
];

// Submissions now carry claimant + beneficiaries[] (spec §7) instead of a single "member",
// and support NeedsEvidence / Resubmitted states (spec §9) with a reviewerComment.
const initialSubmissions = [
  { id: "s0", challengeId: "c0", taskId: "t0b", team: "Indiana Squad", claimant: "Paul Gregg", beneficiaries: ["Paul Gregg", "Saurabh Chaudhary"], fileName: "top5-prompts.docx", fileType: "doc", comment: "Indiana Jones themed prompt pack", status: "Approved", xp: 40, submittedAt: "17 Jul", reviewerComment: "" },
  { id: "s0b", challengeId: "c0b", taskId: "t0d", team: "Prompt Pairs", claimant: "Kavita Khanna", beneficiaries: ["Kavita Khanna"], fileName: "wireless-doc.png", fileType: "image", comment: "Number 5 entry", status: "Approved", xp: 10, submittedAt: "7 Jul", reviewerComment: "" },
  { id: "s1", challengeId: "c1", taskId: "t1", team: "AI-Migos", claimant: "Pooja Jhawar", beneficiaries: ["Pooja Jhawar"], fileName: "diane-idea.png", fileType: "image", comment: "Escape room themed around AI agents", status: "Approved", xp: 10, submittedAt: "20 Aug 09:12", reviewerComment: "" },
  { id: "s2", challengeId: "c2", taskId: "t4", team: "Bulls-AI", claimant: "Kavita Khanna", beneficiaries: ["Kavita Khanna", "Saurabh Chaudhary", "Yanlin Gong"], fileName: "team-photo.jpg", fileType: "image", comment: "Team formed, mission attached", status: "Approved", xp: 10, submittedAt: "19 Aug 13:21", reviewerComment: "" },
  { id: "s3", challengeId: "c2", taskId: "t5", team: "Threebotics", claimant: "Divya Varghese", beneficiaries: ["Divya Varghese", "Keerthana Manogaran", "Yasir"], fileName: "enrolment.png", fileType: "doc", comment: "Claiming for whole team — Threebotics", status: "Needs Evidence", xp: 0, submittedAt: "19 Aug 14:00", reviewerComment: "Please show enrolment proof for all 3 members." },
  { id: "s4", challengeId: "c2", taskId: "t6", team: "AI-Migos", claimant: "Suhas Kesarla", beneficiaries: ["Suhas Kesarla"], fileName: "pulsebot.mp4", fileType: "video", comment: "Self-hosted reminder bot, Teams integration", status: "Under Review", xp: 0, submittedAt: "20 Aug 08:40", reviewerComment: "" },
  { id: "s5", challengeId: "c3", taskId: "t7", team: "Bulls-AI", claimant: "Saurabh Chaudhary", beneficiaries: ["Saurabh Chaudhary"], fileName: "raid-proof.png", fileType: "image", comment: "Lobby 2, 12 points", status: "Rejected", xp: 0, submittedAt: "13 Aug", reviewerComment: "Screenshot doesn't show lobby assignment — please resend." },
  { id: "s6", challengeId: "c0", taskId: "t0c", team: "Indiana Squad", claimant: "Saurabh Chaudhary", beneficiaries: ["Paul Gregg", "Saurabh Chaudhary"], fileName: "lost-meeting-notes-final.mp4", fileType: "video", comment: "Added the remaining artifact as requested", status: "Resubmitted", xp: 0, submittedAt: "Just now", reviewerComment: "Please also submit the remaining artifact for all the points." },
];

const initialAwards = [
  { id: "a1", cycleId: "aug26", member: "Angela Kaur", categoryCode: "RAID", reason: "Remote raid, Lobby 1 participation", xp: 15, awardedAt: "11 Aug" },
  { id: "a2", cycleId: "aug26", member: "Kanika Mehta", categoryCode: "FRIDAY_FUNNY", reason: "Funniest David Preiss birthday wish", xp: 5, awardedAt: "7 Aug" },
  { id: "a3", cycleId: "jul26", member: "Suhas Kesarla", categoryCode: "EARLY_BIRD", reason: "Early bird bonus", xp: 10, awardedAt: "3 Jul" },
  { id: "a4", cycleId: "jul26", member: "Suhas Kesarla", categoryCode: "BUDDY_ENROLMENT", reason: "Buddy enrolment bonus", xp: 20, awardedAt: "5 Jul" },
];

// Raid passes are a separate tracked resource, not XP (spec §5) — deliberately not folded
// into the ledger totals below.
const RAID_PASSES = {
  aug26: [
    { name: "Angela Kaur", physicalAssigned: 4, physicalUsed: 1, remoteAssigned: 1, remoteUsed: 0 },
    { name: "Suhas Kesarla", physicalAssigned: 4, physicalUsed: 2, remoteAssigned: 1, remoteUsed: 0 },
    { name: "Saurabh Chaudhary", physicalAssigned: 4, physicalUsed: 3, remoteAssigned: 1, remoteUsed: 1 },
    { name: "Kavita Khanna", physicalAssigned: 4, physicalUsed: 1, remoteAssigned: 1, remoteUsed: 0 },
  ],
};

const weeklyParticipation = [
  { week: "Jul Wk3", submissions: 18 },
  { week: "Jul Wk4", submissions: 21 },
  { week: "Aug Wk1", submissions: 14 },
  { week: "Aug Wk2", submissions: 22 },
  { week: "Aug Wk3", submissions: 27 },
];

const NAV = [
  { id: "dashboard", label: "Dashboard", icon: LayoutDashboard, roles: ["participant", "manager"] },
  { id: "challenges", label: "Challenges", icon: Swords, roles: ["participant", "manager"] },
  { id: "newchallenge", label: "New challenge", icon: Wand2, roles: ["manager"] },
  { id: "submit", label: "Submit work", icon: UploadCloud, roles: ["participant"] },
  { id: "activity", label: "My activity", icon: ListChecks, roles: ["participant"] },
  { id: "team", label: "My team", icon: Users, roles: ["participant"] },
  { id: "leaderboard", label: "Leaderboard", icon: Trophy, roles: ["participant", "manager"] },
  { id: "review", label: "Review queue", icon: ClipboardCheck, roles: ["manager"] },
  { id: "scoresheet", label: "Scoresheet", icon: Table, roles: ["manager"] },
  { id: "analytics", label: "Analytics", icon: BarChart3, roles: ["manager"] },
];

const evidenceIcon = (type) => {
  const normalized = String(type || "").toLowerCase();
  if (normalized === "image") return ImageIcon;
  if (normalized === "video") return Video;
  if (normalized === "link") return Send;
  if (normalized === "attachment" || normalized === "multiple" || normalized === "custom") return Paperclip;
  if (normalized === "none") return CheckCircle2;
  return FileText;
};

const STATUS_STYLE = {
  "Approved": { color: C.green, icon: CheckCircle2 },
  "Rejected": { color: C.red, icon: XCircle },
  "Under Review": { color: "#8A6416", icon: Hourglass },
  "Needs Evidence": { color: C.orange, icon: AlertTriangle },
  "Resubmitted": { color: C.blue, icon: RotateCcw },
};
const statusStyle = (s) => STATUS_STYLE[s] || STATUS_STYLE["Under Review"];

const SCORING_MODE_LABEL = {
  "individual": "Individual",
  "whole-team": "Whole team",
  "claimant-selects": "You choose who benefits",
  "attendance": "Attendance (manager-recorded)",
};

function Badge({ children, tone = "blue" }) {
  const map = {
    blue: { bg: C.blueDim, fg: C.blue },
    gold: { bg: C.goldDim, fg: "#8A6416" },
    green: { bg: C.greenDim, fg: C.green },
    red: { bg: C.redDim, fg: C.red },
    purple: { bg: C.purpleDim, fg: C.purple },
    teal: { bg: C.tealDim, fg: C.teal },
    orange: { bg: C.orangeDim, fg: C.orange },
    muted: { bg: C.surfaceMuted, fg: C.muted },
  };
  const t = map[tone] || map.blue;
  return (
    <span style={{
      background: t.bg, color: t.fg, fontFamily: fontMono, fontSize: 11,
      fontWeight: 600, padding: "3px 9px", borderRadius: 999,
      letterSpacing: "0.03em", whiteSpace: "nowrap",
    }}>
      {children}
    </span>
  );
}

function Panel({ children, style, accent }) {
  return (
    <div style={{
      background: C.surface, border: `1px solid ${C.border}`, borderRadius: 10,
      borderTop: accent ? `3px solid ${accent}` : `1px solid ${C.border}`,
      padding: "20px 22px", boxShadow: "0 1px 2px rgba(10,31,68,0.04)", ...style,
    }}>
      {children}
    </div>
  );
}

function SectionTitle({ eyebrow, title, action }) {
  return (
    <div style={{ display: "flex", alignItems: "flex-end", justifyContent: "space-between", marginBottom: 16, gap: 12 }}>
      <div>
        {eyebrow && (
          <div style={{ fontFamily: fontMono, fontSize: 11, color: C.blue, letterSpacing: "0.08em", marginBottom: 4, fontWeight: 700 }}>
            {eyebrow}
          </div>
        )}
        <h2 style={{ fontFamily: fontDisplay, fontSize: 22, fontWeight: 700, margin: 0, color: C.navy }}>
          {title}
        </h2>
      </div>
      {action}
    </div>
  );
}

function CycleBadge({ status }) {
  if (status === "active") return <Badge tone="gold">Live cycle</Badge>;
  return (
    <span style={{
      background: C.surfaceMuted, color: C.muted, fontFamily: fontMono, fontSize: 11,
      fontWeight: 600, padding: "3px 9px", borderRadius: 999, letterSpacing: "0.03em",
      border: `1px solid ${C.border}`,
    }}>
      Closed
    </span>
  );
}

const selectStyle = {
  width: "100%", background: C.surfaceMuted, border: `1px solid ${C.border}`,
  borderRadius: 8, color: C.navy, fontSize: 13, padding: "9px 12px",
};

function Field({ label, children }) {
  return (
    <div style={{ marginBottom: 14 }}>
      <div style={{ fontSize: 11.5, color: C.muted, marginBottom: 6 }}>{label}</div>
      {children}
    </div>
  );
}

export default function PASQuestPortal() {
  const [role, setRole] = useState("participant");
  const [tab, setTab] = useState("dashboard");
  const [challenges, setChallenges] = useState(initialChallenges);
  const [teams, setTeams] = useState(initialTeams);
  const [submissions, setSubmissions] = useState(initialSubmissions);
  const [awards, setAwards] = useState(initialAwards);
  const [toast, setToast] = useState(null);
  const [selectedCycle, setSelectedCycle] = useState(CURRENT_CYCLE);

  const showToast = (msg) => {
    setToast(msg);
    setTimeout(() => setToast(null), 2800);
  };

  const cycleChallenges = useMemo(
    () => selectedCycle === "all" ? challenges : challenges.filter((c) => c.cycleId === selectedCycle),
    [challenges, selectedCycle]
  );
  // Challenges still accepting submissions from an earlier cycle, visible while browsing a later
  // one — the visible proof of the core fix: calendar month must not gate eligibility.
  const overlappingOpenChallenges = useMemo(
    () => selectedCycle === "all" ? [] : challenges.filter((c) => c.cycleId !== selectedCycle && c.status === "open"),
    [challenges, selectedCycle]
  );
  const cycleTeams = useMemo(
    () => selectedCycle === "all" ? teams : teams.filter((t) => t.cycleId === selectedCycle),
    [teams, selectedCycle]
  );
  const cycleSubmissions = useMemo(() => {
    if (selectedCycle === "all") return submissions;
    const idsInCycle = new Set(challenges.filter((c) => c.cycleId === selectedCycle).map((c) => c.id));
    return submissions.filter((s) => idsInCycle.has(s.challengeId));
  }, [submissions, challenges, selectedCycle]);
  const cycleAwards = useMemo(
    () => selectedCycle === "all" ? awards : awards.filter((a) => a.cycleId === selectedCycle),
    [awards, selectedCycle]
  );

  // Submitting is gated by challenge.status === "open", never by cycle — this is the actual
  // fix. Every open challenge is submittable regardless of which cycle it reports against.
  const openChallenges = useMemo(() => challenges.filter((c) => c.status === "open"), [challenges]);
  const writeTeams = useMemo(() => teams.filter((t) => t.cycleId === CURRENT_CYCLE), [teams]);
  const myTeamCurrent = writeTeams.find((t) => t.members.includes(CURRENT_USER.name));
  const myTeamViewed = cycleTeams.find((t) => t.members.includes(CURRENT_USER.name));

  // Full cycle roster (spec §3 CycleParticipant) — includes zero-XP people, not just those
  // with team/submission/award activity.
  const rosterForCycle = useMemo(() => {
    if (selectedCycle === "all") {
      const all = new Set();
      Object.values(CYCLE_ROSTER).forEach((list) => list.forEach((n) => all.add(n)));
      return [...all];
    }
    return CYCLE_ROSTER[selectedCycle] || [];
  }, [selectedCycle]);

  const memberPoints = useMemo(() => {
    const totals = {};
    rosterForCycle.forEach((n) => (totals[n] = 0));
    cycleSubmissions.forEach((s) => {
      if (s.status === "Approved") {
        s.beneficiaries.forEach((b) => { totals[b] = (totals[b] || 0) + s.xp; });
      }
    });
    cycleAwards.forEach((a) => { totals[a.member] = (totals[a.member] || 0) + a.xp; });
    return totals;
  }, [cycleSubmissions, cycleAwards, rosterForCycle]);

  const myPoints = memberPoints[CURRENT_USER.name] || 0;
  const pendingReviewCount = submissions.filter((s) => s.status === "Under Review" || s.status === "Resubmitted").length;

  const approveSubmission = (id, xp) => {
    setSubmissions((prev) => prev.map((s) => (s.id === id ? { ...s, status: "Approved", xp, reviewerComment: "" } : s)));
    showToast("Approved — every beneficiary awarded XP");
  };
  const rejectSubmission = (id) => {
    setSubmissions((prev) => prev.map((s) => (s.id === id ? { ...s, status: "Rejected", xp: 0 } : s)));
    showToast("Submission rejected");
  };
  const requestMoreEvidence = (id, comment) => {
    setSubmissions((prev) => prev.map((s) => (s.id === id ? { ...s, status: "Needs Evidence", xp: 0, reviewerComment: comment } : s)));
    showToast("Requested more evidence");
  };
  const resubmit = (id, comment) => {
    setSubmissions((prev) => prev.map((s) => (s.id === id ? { ...s, status: "Resubmitted", comment: comment || s.comment, submittedAt: "Just now" } : s)));
    showToast("Resubmitted for review");
  };
  const awardXP = ({ member, categoryCode, reason, xp, cycleId }) => {
    if (!cycleId) {
      showToast("Choose a specific reporting cycle before awarding XP");
      return;
    }
    setAwards((prev) => [
      { id: `a${prev.length + 1}`, cycleId, member, categoryCode, reason, xp, awardedAt: "Just now" },
      ...prev,
    ]);
    showToast(`${xp} XP awarded to ${member} for ${CYCLES.find((c) => c.id === cycleId)?.label || cycleId}`);
  };
  const createChallenge = (challenge) => {
    setChallenges((prev) => [...prev, { ...challenge, id: `c${prev.length + 1}`, cycleId: CURRENT_CYCLE, status: "open" }]);
    showToast("Challenge published to the portal — Teams announcement queued");
    setTab("challenges");
  };

  const navItems = NAV.filter((n) => n.roles.includes(role));
  const activeCycleMeta = CYCLES.find((c) => c.id === selectedCycle);

  return (
    <div className="pq-shell" style={{
      fontFamily: fontBody, background: C.bg, color: C.text, minHeight: 720,
      borderRadius: 12, overflow: "hidden", border: `1px solid ${C.border}`,
    }}>
      <style>{`
        @import url('https://fonts.googleapis.com/css2?family=Sora:wght@600;700;800&family=Inter:wght@400;500;600&family=JetBrains+Mono:wght@500;600&display=swap');
        * { box-sizing: border-box; }
        button { font-family: inherit; cursor: pointer; }
        .navbtn:hover { background: #EEF2F8 !important; }
        .card:hover { border-color: ${C.borderStrong} !important; box-shadow: 0 2px 8px rgba(10,31,68,0.08) !important; }
        select { appearance: none; }

        .pq-shell { display: flex; flex-direction: row; }
        .pq-sidebar { width: 220px; flex-shrink: 0; flex-direction: column; }
        .pq-brand-text { display: block; }
        .pq-navlist { flex-direction: column; }
        .pq-navlabel { display: inline; }
        .pq-viewas { margin-top: auto; padding-top: 16px; border-top: 1px solid rgba(255,255,255,0.14); }
        .pq-main { padding: 24px 30px; }
        .pq-grid-4 { display: grid; grid-template-columns: repeat(4, 1fr); gap: 14px; }
        .pq-grid-3 { display: grid; grid-template-columns: repeat(3, 1fr); gap: 14px; }
        .pq-grid-2 { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; align-items: start; }

        @media (max-width: 760px) {
          .pq-shell { flex-direction: column; }
          .pq-sidebar { width: 100%; flex-direction: row; align-items: center; padding: 10px 12px !important; overflow-x: auto; gap: 10px; }
          .pq-brand { padding: 0 !important; margin-right: 4px; flex-shrink: 0; }
          .pq-brand-text { display: none; }
          .pq-navlist { flex-direction: row; gap: 4px; flex-shrink: 0; }
          .pq-navbtn { white-space: nowrap; padding: 8px 10px !important; }
          .pq-navlabel { display: none; }
          .pq-viewas { margin-top: 0; margin-left: auto; padding-top: 0; border-top: none; flex-shrink: 0; }
          .pq-main { padding: 14px !important; }
          .pq-grid-4 { grid-template-columns: repeat(2, 1fr); }
          .pq-grid-3 { grid-template-columns: 1fr; }
          .pq-grid-2 { grid-template-columns: 1fr; }
        }
        @media (max-width: 420px) {
          .pq-grid-4 { grid-template-columns: 1fr; }
        }
      `}</style>

      {/* Sidebar */}
      <div className="pq-sidebar" style={{ background: C.navy, padding: "22px 14px", display: "flex" }}>
        <div className="pq-brand" style={{ padding: "0 10px 22px", display: "flex", alignItems: "center", gap: 8 }}>
          <div style={{ width: 30, height: 30, borderRadius: 6, background: C.gold, display: "flex", alignItems: "center", justifyContent: "center", fontFamily: fontDisplay, fontWeight: 800, fontSize: 13, color: C.navy, flexShrink: 0 }}>PAS</div>
          <div className="pq-brand-text">
            <div style={{ fontFamily: fontMono, fontSize: 9, color: C.gold, letterSpacing: "0.1em" }}>AI</div>
            <div style={{ fontFamily: fontDisplay, fontSize: 15, fontWeight: 800, color: "#fff", letterSpacing: "-0.01em", lineHeight: 1 }}>QUEST</div>
          </div>
        </div>
        <div className="pq-navlist" style={{ display: "flex", gap: 3 }}>
          {navItems.map((n) => {
            const Icon = n.icon;
            const active = tab === n.id;
            return (
              <button
                key={n.id}
                className="pq-navbtn"
                onClick={() => setTab(n.id)}
                style={{
                  display: "flex", alignItems: "center", gap: 10, padding: "9px 12px",
                  borderRadius: 8, border: "none", textAlign: "left",
                  background: active ? C.blue : "transparent",
                  color: active ? "#fff" : "rgba(255,255,255,0.72)", fontSize: 13.5, fontWeight: 500,
                  position: "relative",
                }}
              >
                <Icon size={16} style={{ color: active ? "#fff" : "rgba(255,255,255,0.55)", flexShrink: 0 }} />
                <span className="pq-navlabel">{n.label}</span>
                {n.id === "review" && pendingReviewCount > 0 && (
                  <span style={{
                    marginLeft: "auto", background: C.gold, color: C.navy, fontSize: 10,
                    fontWeight: 700, borderRadius: 999, padding: "1px 6px", fontFamily: fontMono,
                  }}>{pendingReviewCount}</span>
                )}
              </button>
            );
          })}
        </div>

        <div className="pq-viewas">
          <div style={{ fontSize: 10, color: "rgba(255,255,255,0.5)", fontFamily: fontMono, marginBottom: 8, letterSpacing: "0.06em" }}>VIEW AS</div>
          <div style={{ display: "flex", gap: 4, background: "rgba(0,0,0,0.22)", borderRadius: 8, padding: 3 }}>
            {["participant", "manager"].map((r) => (
              <button
                key={r}
                onClick={() => { setRole(r); setTab("dashboard"); }}
                style={{
                  flex: 1, border: "none", borderRadius: 6, padding: "6px 4px", fontSize: 11.5,
                  fontWeight: 600, background: role === r ? C.gold : "transparent",
                  color: role === r ? C.navy : "rgba(255,255,255,0.65)",
                }}
              >
                {r === "participant" ? "Participant" : "Manager"}
              </button>
            ))}
          </div>
        </div>
      </div>

      {/* Main */}
      <div className="pq-main" style={{ flex: 1, overflow: "auto", position: "relative", background: C.bg, minWidth: 0 }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 22, gap: 16, flexWrap: "wrap" }}>
          <div>
            <div style={{ fontSize: 13, color: C.muted }}>
              {role === "manager" ? "Signed in as challenge manager" : `Welcome back, ${CURRENT_USER.name.split(" ")[0]}`}
            </div>
          </div>
          <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
            <div style={{ position: "relative", display: "flex", alignItems: "center" }}>
              <History size={13} style={{ color: C.muted, position: "absolute", left: 12, pointerEvents: "none" }} />
              <select
                value={selectedCycle}
                onChange={(e) => setSelectedCycle(e.target.value)}
                style={{
                  background: C.surface, border: `1px solid ${C.border}`, color: C.navy, fontSize: 12.5,
                  fontWeight: 600, borderRadius: 999, padding: "8px 30px 8px 32px", fontFamily: fontBody,
                }}
              >
                {(role === "manager") && <option value="all">All cycles</option>}
                {CYCLES.slice().reverse().map((c) => (
                  <option key={c.id} value={c.id}>{c.label}{c.id === CURRENT_CYCLE ? " (current)" : ""}</option>
                ))}
              </select>
              <ChevronDown size={13} style={{ color: C.muted, position: "absolute", right: 10, pointerEvents: "none" }} />
            </div>
            {activeCycleMeta && <CycleBadge status={activeCycleMeta.status} />}
            {role === "participant" && (
              <div style={{ display: "flex", alignItems: "center", gap: 10, background: C.surface, border: `1px solid ${C.border}`, borderRadius: 999, padding: "6px 14px 6px 8px" }}>
                <div style={{ width: 26, height: 26, borderRadius: "50%", background: C.blue, display: "flex", alignItems: "center", justifyContent: "center", fontSize: 11, fontWeight: 700, color: "#fff" }}>
                  {CURRENT_USER.name.split(" ").map((p) => p[0]).join("")}
                </div>
                <span style={{ fontFamily: fontMono, fontSize: 13, fontWeight: 700, color: "#8A6416" }}>{myPoints} XP</span>
              </div>
            )}
          </div>
        </div>

        {selectedCycle !== CURRENT_CYCLE && selectedCycle !== "all" && (
          <div style={{
            background: C.goldDim, border: `1px solid #F0D98A`, borderRadius: 8,
            padding: "10px 14px", fontSize: 12.5, color: "#8A6416", marginBottom: 18, display: "flex", alignItems: "center", gap: 8,
          }}>
            <Clock size={14} /> Viewing a closed cycle — this is read-only history. Teams reset each month, but open challenges can still be submitted to regardless of which cycle you're browsing.
          </div>
        )}

        {tab === "dashboard" && (
          <Dashboard
            role={role} challenges={cycleChallenges} submissions={cycleSubmissions} myTeam={myTeamViewed}
            myPoints={myPoints} pendingReviewCount={pendingReviewCount} setTab={setTab}
            cycleLabel={activeCycleMeta?.label} cycleId={selectedCycle} awardsCount={cycleAwards.length}
          />
        )}
        {tab === "challenges" && (
          <ChallengesView
            challenges={cycleChallenges} overlapping={overlappingOpenChallenges} setTab={setTab} role={role}
          />
        )}
        {tab === "newchallenge" && <CreateChallengeView onCreate={createChallenge} />}
        {tab === "submit" && (
          <SubmitView challenges={openChallenges} teams={teams} setSubmissions={setSubmissions} showToast={showToast} />
        )}
        {tab === "activity" && (
          <MyActivityView
            submissions={cycleSubmissions} awards={cycleAwards} challenges={cycleChallenges}
            myPoints={myPoints} cycleLabel={activeCycleMeta?.label} onResubmit={resubmit}
          />
        )}
        {tab === "team" && (
          <TeamView
            teams={cycleTeams} myTeam={myTeamViewed} setTeams={setTeams} showToast={showToast}
            cycleLabel={activeCycleMeta?.label} isCurrentCycle={selectedCycle === CURRENT_CYCLE}
          />
        )}
        {tab === "leaderboard" && <LeaderboardView memberPoints={memberPoints} teams={cycleTeams} />}
        {tab === "review" && (
          <ReviewView
            submissions={cycleSubmissions} challenges={cycleChallenges}
            onApprove={approveSubmission} onReject={rejectSubmission} onRequestEvidence={requestMoreEvidence}
            awards={cycleAwards} onAwardXP={awardXP}
            rosterHint={rosterForCycle}
            awardCycleId={selectedCycle === "all" ? null : selectedCycle}
            awardCycleLabel={selectedCycle === "all" ? null : activeCycleMeta?.label}
          />
        )}
        {tab === "scoresheet" && (
          <ScoresheetView
            challenges={cycleChallenges} roster={rosterForCycle} submissions={cycleSubmissions} awards={cycleAwards}
            cycleLabel={activeCycleMeta?.label} raidPasses={selectedCycle === "all" ? [] : (RAID_PASSES[selectedCycle] || [])}
          />
        )}
        {tab === "analytics" && <AnalyticsView submissions={submissions} teams={teams} challenges={challenges} cycles={CYCLES} />}

        {toast && (
          <div style={{
            position: "fixed", bottom: 24, right: 34, background: C.navy, border: `1px solid ${C.navyLight}`,
            borderRadius: 8, padding: "11px 16px", display: "flex", alignItems: "center", gap: 8,
            fontSize: 13, color: "#fff", boxShadow: "0 8px 24px rgba(10,31,68,0.3)",
          }}>
            <CheckCircle2 size={15} style={{ color: "#4ADE80" }} />
            {toast}
          </div>
        )}
      </div>
    </div>
  );
}

// ---------------- Dashboard ----------------
function Dashboard({ role, challenges, submissions, myTeam, myPoints, pendingReviewCount, setTab, cycleLabel, cycleId, awardsCount }) {
  const characters = cycleId === "all" ? Object.values(CHARACTER_ROSTER_BY_CYCLE).flat() : (CHARACTER_ROSTER_BY_CYCLE[cycleId] || []);

  if (role === "manager") {
    const totalSubs = submissions.length;
    return (
      <div>
        <SectionTitle eyebrow={cycleLabel?.toUpperCase()} title="Quest control room" />
        <div className="pq-grid-4" style={{ marginBottom: 22 }}>
          <StatCard label="Challenges this cycle" value={challenges.length} icon={Swords} tone="blue" />
          <StatCard label="Pending review" value={pendingReviewCount} icon={Hourglass} tone="gold" />
          <StatCard label="Submissions this cycle" value={totalSubs} icon={UploadCloud} tone="purple" />
          <StatCard label="Bonus XP awards" value={awardsCount || 0} icon={Sparkles} tone="green" />
        </div>
        <Panel accent={C.blue}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 14 }}>
            <div style={{ fontFamily: fontDisplay, fontWeight: 700, fontSize: 15, color: C.navy }}>Needs your attention</div>
            <button onClick={() => setTab("review")} style={{ background: "none", border: "none", color: C.blue, fontSize: 12.5, display: "flex", alignItems: "center", gap: 4, fontWeight: 600 }}>
              Open review queue <ChevronRight size={13} />
            </button>
          </div>
          {submissions.filter((s) => s.status === "Under Review" || s.status === "Resubmitted").length === 0 && (
            <div style={{ color: C.muted, fontSize: 13 }}>Nothing pending in this cycle.</div>
          )}
          {submissions.filter((s) => s.status === "Under Review" || s.status === "Resubmitted").slice(0, 3).map((s) => (
            <SubmissionRow key={s.id} s={s} challenges={challenges} compact />
          ))}
        </Panel>
      </div>
    );
  }

  return (
    <div>
      <SectionTitle eyebrow={cycleLabel ? `${cycleLabel.toUpperCase()} · YOUR QUEST` : "YOUR QUEST"} title="Dashboard" />
      <div className="pq-grid-3" style={{ marginBottom: 22 }}>
        <StatCard label="Your XP this cycle" value={myPoints} icon={Flame} tone="gold" />
        <StatCard label="Your team" value={myTeam ? myTeam.name : "No team yet"} icon={Users} tone="blue" small />
        <StatCard label="Active challenges" value={challenges.length} icon={Swords} tone="purple" />
      </div>
      <Panel style={{ marginBottom: 18 }} accent={C.blue}>
        <div style={{ fontFamily: fontDisplay, fontWeight: 700, fontSize: 15, marginBottom: 14, color: C.navy }}>What's due</div>
        {challenges.length === 0 && <div style={{ color: C.muted, fontSize: 13 }}>No challenges in this cycle.</div>}
        {challenges.map((c) => (
          <div key={c.id} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "10px 0", borderBottom: `1px solid ${C.border}` }}>
            <div>
              <div style={{ fontSize: 13.5, fontWeight: 500, color: C.navy }}>{c.name}</div>
              <div style={{ fontSize: 12, color: C.muted, marginTop: 2 }}>{c.tasks.length} tasks · due {c.due}{c.status === "open" ? "" : " · closed"}</div>
            </div>
            <Badge tone="gold">{c.tasks.reduce((a, t) => a + t.xp, 0)} XP</Badge>
          </div>
        ))}
      </Panel>
      <Panel accent={C.purple}>
        <div style={{ fontFamily: fontDisplay, fontWeight: 700, fontSize: 15, marginBottom: 6, color: C.navy }}>This cycle's characters</div>
        <div style={{ fontSize: 12.5, color: C.muted, marginBottom: 14 }}>
          {characters.length > 0 ? "Available characters for this cycle's theme. Individual challenges/announcements may assign them differently." : `No named characters are configured for ${cycleLabel}.`}
        </div>
        {characters.length > 0 && (
          <div style={{ display: "flex", gap: 10 }}>
            {characters.map((g) => (
              <div key={g.name} style={{ flex: 1, background: C.purpleDim, border: `1px solid ${C.border}`, borderRadius: 8, padding: "10px 12px", textAlign: "center" }}>
                <Sparkles size={14} style={{ color: C.purple, marginBottom: 6 }} />
                <div style={{ fontSize: 12.5, fontWeight: 600, color: C.navy }}>{g.name}</div>
                <div style={{ fontSize: 10.5, color: C.muted }}>{g.role}</div>
              </div>
            ))}
          </div>
        )}
      </Panel>
    </div>
  );
}

function StatCard({ label, value, icon: Icon, tone, small }) {
  const map = { gold: C.gold, green: C.green, blue: C.blue, purple: C.purple, teal: C.teal };
  const color = map[tone] || C.blue;
  return (
    <Panel style={{ padding: "16px 18px" }} accent={color}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start" }}>
        <div>
          <div style={{ fontSize: 11.5, color: C.muted, marginBottom: 6 }}>{label}</div>
          <div style={{ fontFamily: small ? fontDisplay : fontMono, fontSize: small ? 16 : 24, fontWeight: 700, color: C.navy }}>{value}</div>
        </div>
        <Icon size={17} style={{ color }} />
      </div>
    </Panel>
  );
}

// ---------------- Challenges ----------------
function ChallengesView({ challenges, overlapping, setTab, role }) {
  return (
    <div>
      <SectionTitle
        eyebrow="QUEST LOG"
        title="Challenges"
        action={
          role === "manager" && (
            <button onClick={() => setTab("newchallenge")} style={{ background: C.navy, border: "none", color: "#fff", fontSize: 12.5, fontWeight: 600, padding: "8px 16px", borderRadius: 999, display: "flex", alignItems: "center", gap: 6 }}>
              <Wand2 size={13} /> New challenge
            </button>
          )
        }
      />

      {overlapping.length > 0 && (
        <div style={{ marginBottom: 20 }}>
          <div style={{
            background: C.orangeDim, border: `1px solid #F0C4A0`, borderRadius: 8, padding: "9px 14px",
            fontSize: 12, color: "#9A4A15", marginBottom: 10, display: "flex", alignItems: "center", gap: 8,
          }}>
            <AlertTriangle size={13} /> Still accepting submissions from an earlier cycle — a challenge's own status decides eligibility, not the calendar month.
          </div>
          {overlapping.map((c) => <ChallengeCard key={c.id} c={c} setTab={setTab} role={role} showSubmit />)}
        </div>
      )}

      {challenges.length === 0 && overlapping.length === 0 && (
        <Panel><div style={{ color: C.muted, fontSize: 13 }}>No challenges in this cycle.</div></Panel>
      )}
      <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
        {challenges.map((c) => <ChallengeCard key={c.id} c={c} setTab={setTab} role={role} showSubmit={c.status === "open"} />)}
      </div>
    </div>
  );
}

function ChallengeCard({ c, setTab, role, showSubmit }) {
  const cat = categoryColor(c.category);
  return (
    <Panel className="card" accent={cat.fg} style={{ transition: "border-color .15s, box-shadow .15s", marginBottom: 14 }}>
      {c.heroImage && (
        <img src={c.heroImage} alt="" style={{ width: "100%", maxHeight: 160, objectFit: "cover", borderRadius: 8, marginBottom: 14 }} />
      )}
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 14 }}>
        <div>
          <span style={{
            fontFamily: fontMono, fontSize: 10.5, color: cat.fg, letterSpacing: "0.06em",
            background: cat.bg, padding: "3px 8px", borderRadius: 999, fontWeight: 700,
          }}>{c.eyebrow}</span>
          {c.status === "closed" && <span style={{ marginLeft: 6 }}><Badge tone="muted">Closed</Badge></span>}
          <div style={{ fontFamily: fontDisplay, fontSize: 17, fontWeight: 700, marginTop: 8, color: C.navy }}>{c.name}</div>
          <div style={{ fontSize: 13, color: C.muted, marginTop: 4, maxWidth: 480 }}>{c.desc}</div>
        </div>
        <div style={{ textAlign: "right", flexShrink: 0 }}>
          <div style={{ fontSize: 11, color: C.muted, marginBottom: 4 }}>Due {c.due}</div>
          <Badge tone="gold">{c.tasks.reduce((a, t) => a + t.xp, 0)} XP max</Badge>
        </div>
      </div>
      <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
        {c.tasks.map((t, i) => {
          const Icon = evidenceIcon(t.evidence);
          return (
            <div key={t.id} style={{ display: "flex", alignItems: "center", gap: 12, background: C.surfaceMuted, borderRadius: 8, padding: "9px 12px", border: `1px solid ${C.border}` }}>
              <div style={{ width: 22, height: 22, borderRadius: "50%", background: cat.fg, display: "flex", alignItems: "center", justifyContent: "center", fontSize: 11, fontWeight: 700, flexShrink: 0, color: "#fff" }}>{i + 1}</div>
              <div style={{ flex: 1, fontSize: 13, color: C.navy }}>{t.name}</div>
              <Icon size={13} style={{ color: C.muted }} />
              <Badge tone="muted">{SCORING_MODE_LABEL[t.scoringMode] || "Individual"}</Badge>
              <Badge>+{t.xp} XP</Badge>
            </div>
          );
        })}
      </div>
      {showSubmit && role === "participant" && (
        <div style={{ marginTop: 14, display: "flex", justifyContent: "flex-end" }}>
          <button onClick={() => setTab("submit")} style={{
            background: C.blue, border: "none", color: "#fff", fontSize: 12.5, fontWeight: 600,
            padding: "8px 16px", borderRadius: 999, display: "flex", alignItems: "center", gap: 6,
          }}>
            Submit for this challenge <ChevronRight size={13} />
          </button>
        </div>
      )}
    </Panel>
  );
}

// ---------------- Create Challenge ----------------
const EVIDENCE_TYPES = ["None", "Text", "Link", "Attachment", "Multiple", "Custom"];
const SCORING_MODES = ["individual", "whole-team", "claimant-selects", "attendance"];

function CreateChallengeView({ onCreate }) {
  const [category, setCategory] = useState("Go Pass");
  const [name, setName] = useState("");
  const [desc, setDesc] = useState("");
  const [due, setDue] = useState("");
  const [heroFile, setHeroFile] = useState(null);
  const [heroPreview, setHeroPreview] = useState(null);
  const [tasks, setTasks] = useState([{ id: "n1", name: "", xp: 10, evidence: "Attachment", scoringMode: "individual" }]);
  const [error, setError] = useState("");
  const [polishing, setPolishing] = useState(false);

  const cat = categoryColor(category);
  const totalXP = tasks.reduce((a, t) => a + (Number(t.xp) || 0), 0);

  const handleHero = (file) => {
    setHeroFile(file);
    setHeroPreview(file && file.type.startsWith("image/") ? URL.createObjectURL(file) : null);
  };

  const updateTask = (id, patch) => setTasks((prev) => prev.map((t) => t.id === id ? { ...t, ...patch } : t));
  const addTask = () => setTasks((prev) => [...prev, { id: `n${prev.length + 1}`, name: "", xp: 5, evidence: "Attachment", scoringMode: "individual" }]);
  const removeTask = (id) => setTasks((prev) => prev.length > 1 ? prev.filter((t) => t.id !== id) : prev);

  const polishWithAI = () => {
    if (!name.trim()) { setError("Give the challenge a name first."); return; }
    setError("");
    setPolishing(true);
    setTimeout(() => {
      setDesc((prev) => prev.trim() ? `${prev.trim()} Bring your best ideas — the quest crew is watching.` : `Put your AI skills to the test with ${name.trim()}. Simple to join, quick to try, and a great excuse to experiment.`);
      setPolishing(false);
    }, 700);
  };

  const handlePublish = () => {
    if (!name.trim()) { setError("Give the challenge a name."); return; }
    if (!due.trim()) { setError("Set a due date."); return; }
    if (tasks.some((t) => !t.name.trim() || !t.xp)) { setError("Every task needs a name and an XP value."); return; }
    setError("");
    onCreate({
      eyebrow: `${category.toUpperCase()} · NEW`,
      name: name.trim(),
      desc: desc.trim() || `Complete the tasks below to earn XP.`,
      category,
      due: due.trim(),
      heroImage: heroPreview,
      tasks: tasks.map((t) => ({ ...t, xp: Number(t.xp) })),
    });
  };

  return (
    <div>
      <SectionTitle eyebrow="AUTHOR ONCE, PUBLISH EVERYWHERE" title="New challenge" />
      <div style={{ background: C.blueDim, border: `1px solid ${C.border}`, borderRadius: 8, padding: "9px 14px", fontSize: 12, color: C.muted, marginBottom: 18 }}>
        Fill this in once — it becomes the portal card on the left, and the same data would auto-post as the Teams announcement on the right once Teams sync is connected.
      </div>
      <div className="pq-grid-2">
        <Panel accent={C.blue}>
          <Field label="Category">
            <select value={category} onChange={(e) => setCategory(e.target.value)} style={selectStyle}>
              {Object.keys(CATEGORY_COLOR).map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
          </Field>
          <Field label="Challenge name">
            <input value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Build a Copilot Studio agent" style={selectStyle} />
          </Field>
          <Field label="Description">
            <textarea value={desc} onChange={(e) => setDesc(e.target.value)} rows={3} placeholder="What should participants do?" style={{ ...selectStyle, resize: "vertical", fontFamily: fontBody }} />
          </Field>
          <button onClick={polishWithAI} disabled={polishing} style={{
            background: C.surface, border: `1px solid ${C.border}`, color: C.purple, fontSize: 12, fontWeight: 600,
            padding: "6px 12px", borderRadius: 999, display: "flex", alignItems: "center", gap: 6, marginTop: -8, marginBottom: 16,
          }}>
            <Wand2 size={12} /> {polishing ? "Polishing wording…" : "Polish wording with AI"}
          </button>
          <Field label="Due date">
            <input value={due} onChange={(e) => setDue(e.target.value)} placeholder="e.g. 31 Aug" style={selectStyle} />
          </Field>
          <Field label="Hero image (optional)">
            <div style={{ ...selectStyle, padding: "9px 12px" }}>
              <input type="file" accept="image/*" onChange={(e) => handleHero(e.target.files[0] || null)} style={{ fontSize: 12.5, color: C.muted, width: "100%" }} />
            </div>
          </Field>

          <div style={{ fontSize: 11.5, color: C.muted, marginBottom: 8, marginTop: 4 }}>Tasks</div>
          {tasks.map((t, i) => (
            <div key={t.id} style={{ display: "flex", flexDirection: "column", gap: 6, marginBottom: 10, padding: "10px", background: C.surfaceMuted, borderRadius: 8, border: `1px solid ${C.border}` }}>
              <div style={{ display: "flex", gap: 8, alignItems: "center" }}>
                <input value={t.name} onChange={(e) => updateTask(t.id, { name: e.target.value })} placeholder={`Task ${i + 1} name`} style={{ ...selectStyle, flex: 1, background: C.surface }} />
                <input type="number" min="1" value={t.xp} onChange={(e) => updateTask(t.id, { xp: e.target.value })} style={{ ...selectStyle, width: 64, background: C.surface }} />
                <button onClick={() => removeTask(t.id)} style={{ background: "none", border: "none", color: C.muted, padding: 4 }}><X size={14} /></button>
              </div>
              <div style={{ display: "flex", gap: 8 }}>
                <select value={t.evidence} onChange={(e) => updateTask(t.id, { evidence: e.target.value })} style={{ ...selectStyle, background: C.surface }}>
                  {EVIDENCE_TYPES.map((et) => <option key={et} value={et}>{et}</option>)}
                </select>
                <select value={t.scoringMode} onChange={(e) => updateTask(t.id, { scoringMode: e.target.value })} style={{ ...selectStyle, background: C.surface }}>
                  {SCORING_MODES.map((sm) => <option key={sm} value={sm}>{SCORING_MODE_LABEL[sm]}</option>)}
                </select>
              </div>
            </div>
          ))}
          <button onClick={addTask} style={{ background: "none", border: `1px dashed ${C.borderStrong}`, color: C.blue, fontSize: 12, fontWeight: 600, padding: "7px 12px", borderRadius: 8, width: "100%", marginBottom: 16 }}>
            <Plus size={12} style={{ verticalAlign: -2, marginRight: 4 }} /> Add task
          </button>

          {error && <div style={{ color: C.red, fontSize: 12.5, marginBottom: 12 }}>{error}</div>}
          <button onClick={handlePublish} style={{ background: C.blue, border: "none", color: "#fff", fontWeight: 700, fontSize: 13.5, padding: "10px 18px", borderRadius: 999, width: "100%" }}>
            Publish challenge
          </button>
        </Panel>

        <div style={{ display: "flex", flexDirection: "column", gap: 14 }}>
          <div>
            <div style={{ fontSize: 11, fontWeight: 700, color: C.muted, letterSpacing: "0.04em", marginBottom: 8 }}>PORTAL CARD PREVIEW</div>
            <Panel accent={cat.fg} style={{ padding: "16px 18px" }}>
              {heroPreview && <img src={heroPreview} alt="" style={{ width: "100%", borderRadius: 8, marginBottom: 12, maxHeight: 120, objectFit: "cover" }} />}
              <span style={{ fontFamily: fontMono, fontSize: 10, color: cat.fg, background: cat.bg, padding: "3px 8px", borderRadius: 999, fontWeight: 700 }}>
                {category.toUpperCase()} · NEW
              </span>
              <div style={{ fontFamily: fontDisplay, fontWeight: 700, fontSize: 15, marginTop: 8, color: C.navy }}>{name || "Challenge name"}</div>
              <div style={{ fontSize: 12.5, color: C.muted, marginTop: 4 }}>{desc || "Description will appear here."}</div>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginTop: 12 }}>
                <span style={{ fontSize: 11, color: C.muted }}>Due {due || "—"}</span>
                <Badge tone="gold">{totalXP} XP max</Badge>
              </div>
            </Panel>
          </div>
          <div>
            <div style={{ display: "flex", alignItems: "center", gap: 6, fontSize: 11, fontWeight: 700, color: C.muted, letterSpacing: "0.04em", marginBottom: 8 }}>
              <Send size={11} /> TEAMS ANNOUNCEMENT PREVIEW
            </div>
            <div style={{ background: "#F3F2F1", border: `1px solid ${C.border}`, borderRadius: 8, padding: "14px 16px" }}>
              <div style={{ fontSize: 11, color: "#616161", marginBottom: 8 }}>PAS AI Quest 1 · Preety Agarwal</div>
              {heroPreview && <img src={heroPreview} alt="" style={{ width: "100%", borderRadius: 6, marginBottom: 10, maxHeight: 110, objectFit: "cover" }} />}
              <div style={{ fontFamily: fontDisplay, fontWeight: 700, fontSize: 14, color: "#252423" }}>
                {category === "Go Pass" ? "🚀 " : category === "Raid" ? "⚔️ " : "🎉 "}{name || "Challenge name"}
              </div>
              <div style={{ fontSize: 12.5, color: "#3B3A39", marginTop: 4 }}>{desc || "Description will appear here."}</div>
              <div style={{ marginTop: 10, display: "flex", flexDirection: "column", gap: 4 }}>
                {tasks.filter((t) => t.name.trim()).map((t, i) => (
                  <div key={t.id} style={{ fontSize: 12.5, color: "#252423" }}>{i + 1}. {t.name} — <strong>+{t.xp || 0} XP</strong></div>
                ))}
              </div>
              <div style={{ fontSize: 11.5, color: "#616161", marginTop: 10 }}>Due {due || "—"} · Submit via the PAS AI Quest portal</div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

// ---------------- Submit ----------------
function SubmitView({ challenges, teams, setSubmissions, showToast }) {
  const [challengeId, setChallengeId] = useState(challenges[0]?.id || "");
  const [taskId, setTaskId] = useState(challenges[0]?.tasks[0]?.id || "");
  const [files, setFiles] = useState([]);
  const [textResponse, setTextResponse] = useState("");
  const [linkValue, setLinkValue] = useState("");
  const [comment, setComment] = useState("");
  const [selectedBeneficiaries, setSelectedBeneficiaries] = useState([CURRENT_USER.name]);
  const [error, setError] = useState("");

  const challenge = challenges.find((c) => c.id === challengeId);
  const task = challenge?.tasks.find((t) => t.id === taskId);
  // Resolve claimant's team WITHIN THIS CHALLENGE'S OWN CYCLE — not the currently-browsed cycle.
  // This is what makes a still-open July challenge usable while looking at August.
  const teamForChallenge = challenge ? teams.filter((t) => t.cycleId === challenge.cycleId).find((t) => t.members.includes(CURRENT_USER.name)) : null;

  const handleChallengeChange = (id) => {
    setChallengeId(id);
    const c = challenges.find((c) => c.id === id);
    setTaskId(c.tasks[0].id);
    setFiles([]); setTextResponse(""); setLinkValue(""); setError("");
  };

  React.useEffect(() => {
    if (!task) return;
    if (task.scoringMode === "whole-team" && teamForChallenge) {
      setSelectedBeneficiaries(teamForChallenge.members);
    } else {
      setSelectedBeneficiaries([CURRENT_USER.name]);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [taskId, challengeId]);

  const toggleBeneficiary = (name) => {
    setSelectedBeneficiaries((prev) => prev.includes(name) ? prev.filter((n) => n !== name) : [...prev, name]);
  };

  const classify = (file) => {
    if (file.type.startsWith("image/")) return "image";
    if (file.type.startsWith("video/")) return "video";
    return "doc";
  };
  const addFiles = (fileList) => {
    const added = Array.from(fileList).map((f) => ({
      id: `${f.name}-${f.size}-${f.lastModified}`, file: f, name: f.name, type: classify(f),
      preview: f.type.startsWith("image/") ? URL.createObjectURL(f) : null,
    }));
    setFiles((prev) => [...prev, ...added.filter((a) => !prev.some((p) => p.id === a.id))]);
  };
  const removeFile = (id) => setFiles((prev) => prev.filter((f) => f.id !== id));

  const handleSubmit = () => {
    if (!challenge || !task) { setError("Pick a challenge and task first."); return; }
    if (task.scoringMode === "attendance") { setError("Attendance is recorded by the Quest Manager — no participant submission is required."); return; }

    const requirement = task.evidence || "Attachment";
    if (requirement === "Text" && !textResponse.trim()) { setError("Enter the required text response."); return; }
    if (requirement === "Link" && !linkValue.trim()) { setError("Enter the required link."); return; }
    if (requirement === "Attachment" && files.length === 0) { setError("Attach at least one file."); return; }
    if (requirement === "Multiple" && files.length === 0 && !textResponse.trim() && !linkValue.trim()) {
      setError("Add at least one evidence item: text, link, or attachment."); return;
    }
    if (requirement === "Custom" && files.length === 0 && !textResponse.trim() && !linkValue.trim()) {
      setError("Add evidence following the manager's custom instruction."); return;
    }
    if (selectedBeneficiaries.length === 0) { setError("Choose at least one person this submission is for."); return; }

    setError("");
    setSubmissions((prev) => [
      {
        id: `s${prev.length + 1}`, challengeId, taskId, team: teamForChallenge?.name || "No team",
        claimant: CURRENT_USER.name, beneficiaries: selectedBeneficiaries,
        fileName: files.map((f) => f.name).join(", "),
        fileType: files[0]?.type || (linkValue.trim() ? "link" : "text"),
        textResponse: textResponse.trim(),
        links: linkValue.trim() ? [linkValue.trim()] : [],
        comment,
        status: "Under Review", xp: 0, submittedAt: "Just now", reviewerComment: "",
      },
      ...prev,
    ]);
    setFiles([]); setTextResponse(""); setLinkValue(""); setComment("");
    showToast("Submission received — status: Under Review");
  };

  if (!challenge) {
    return (
      <div>
        <SectionTitle eyebrow="EVIDENCE" title="Submit work" />
        <Panel><div style={{ color: C.muted, fontSize: 13 }}>No open challenges right now.</div></Panel>
      </div>
    );
  }

  return (
    <div>
      <SectionTitle eyebrow="EVIDENCE" title="Submit work" />
      <div style={{ background: C.blueDim, border: `1px solid ${C.border}`, borderRadius: 8, padding: "9px 14px", fontSize: 12, color: C.muted, marginBottom: 18, display: "flex", alignItems: "center", gap: 8 }}>
        <Send size={13} /> Every open challenge is submittable here, regardless of which cycle it belongs to — a July challenge extended into August still shows up below.
      </div>
      <Panel style={{ maxWidth: 560 }} accent={C.blue}>
        <Field label="Challenge">
          <select value={challengeId} onChange={(e) => handleChallengeChange(e.target.value)} style={selectStyle}>
            {challenges.map((c) => {
              const cyMeta = CYCLES.find((cy) => cy.id === c.cycleId);
              return <option key={c.id} value={c.id}>{c.name} ({cyMeta?.label}{c.cycleId !== CURRENT_CYCLE ? " · still open" : ""})</option>;
            })}
          </select>
        </Field>
        <Field label="Task">
          <select value={taskId} onChange={(e) => setTaskId(e.target.value)} style={selectStyle}>
            {challenge.tasks.map((t) => <option key={t.id} value={t.id}>{t.name} (+{t.xp} XP)</option>)}
          </select>
        </Field>
        <Field label="Team (resolved for this challenge's own cycle)">
          <div style={{ ...selectStyle, display: "flex", alignItems: "center", color: C.muted }}>{teamForChallenge ? teamForChallenge.name : "You weren't on a team that cycle — submitting as an individual"}</div>
        </Field>
        {task && (task.scoringMode === "whole-team" || task.scoringMode === "claimant-selects") && (
          <Field label={task.scoringMode === "whole-team" ? "This submission claims XP for your whole team" : "Choose who this submission claims XP for"}>
            {teamForChallenge ? (
              <div style={{ display: "flex", flexDirection: "column", gap: 6 }}>
                {teamForChallenge.members.map((m) => (
                  <label key={m} style={{ display: "flex", alignItems: "center", gap: 8, fontSize: 13, color: C.navy, background: C.surfaceMuted, border: `1px solid ${C.border}`, borderRadius: 8, padding: "7px 10px" }}>
                    <input
                      type="checkbox"
                      checked={selectedBeneficiaries.includes(m)}
                      disabled={task.scoringMode === "whole-team"}
                      onChange={() => toggleBeneficiary(m)}
                    />
                    {m}{m === CURRENT_USER.name ? " (you)" : ""}
                  </label>
                ))}
              </div>
            ) : (
              <div style={{ fontSize: 12.5, color: C.muted }}>No team this cycle — submitting for yourself only.</div>
            )}
          </Field>
        )}
        {task?.scoringMode === "attendance" ? (
          <div style={{ background: C.tealDim, border: `1px solid ${C.border}`, borderRadius: 8, padding: "12px 14px", fontSize: 12.5, color: C.muted, marginBottom: 14 }}>
            <ShieldCheck size={14} style={{ color: C.teal, verticalAlign: -2, marginRight: 6 }} />
            Attendance for this task is recorded by the Quest Manager. No participant submission is required.
          </div>
        ) : (
          <>
            <div style={{ fontSize: 11.5, color: C.muted, marginBottom: 8 }}>
              Evidence requirement: <strong style={{ color: C.navy }}>{task?.evidence || "Attachment"}</strong>
              {task?.evidence === "Custom" ? " — follow the manager's instruction; this prototype allows text, link and attachment inputs." : ""}
            </div>

            {(task?.evidence === "Text" || task?.evidence === "Multiple" || task?.evidence === "Custom") && (
              <Field label="Text response">
                <textarea value={textResponse} onChange={(e) => setTextResponse(e.target.value)} rows={3}
                  placeholder="Enter your evidence or explanation"
                  style={{ ...selectStyle, resize: "vertical", fontFamily: fontBody }} />
              </Field>
            )}

            {(task?.evidence === "Link" || task?.evidence === "Multiple" || task?.evidence === "Custom") && (
              <Field label="Evidence link">
                <input value={linkValue} onChange={(e) => setLinkValue(e.target.value)} placeholder="https://..." style={selectStyle} />
              </Field>
            )}

            {(task?.evidence === "Attachment" || task?.evidence === "Multiple" || task?.evidence === "Custom") && (
              <Field label="Attachments">
                <label style={{
                  ...selectStyle, padding: "16px 12px", display: "flex", flexDirection: "column", alignItems: "center",
                  gap: 6, cursor: "pointer", border: `1.5px dashed ${C.borderStrong}`, background: C.surfaceMuted,
                }}>
                  <Paperclip size={16} style={{ color: C.muted }} />
                  <span style={{ fontSize: 12, color: C.muted }}>Click to attach files — images, documents, or video</span>
                  <input type="file" multiple onChange={(e) => addFiles(e.target.files)} style={{ display: "none" }} />
                </label>
                {files.length > 0 && (
                  <div style={{ display: "flex", flexWrap: "wrap", gap: 8, marginTop: 10 }}>
                    {files.map((f) => {
                      const Icon = evidenceIcon(f.type);
                      return (
                        <div key={f.id} style={{ position: "relative", width: 72 }}>
                          {f.preview ? (
                            <img src={f.preview} alt="" style={{ width: 72, height: 72, objectFit: "cover", borderRadius: 8, border: `1px solid ${C.border}` }} />
                          ) : (
                            <div style={{ width: 72, height: 72, borderRadius: 8, border: `1px solid ${C.border}`, background: C.surface, display: "flex", alignItems: "center", justifyContent: "center" }}>
                              <Icon size={20} style={{ color: C.muted }} />
                            </div>
                          )}
                          <button onClick={() => removeFile(f.id)} style={{
                            position: "absolute", top: -6, right: -6, background: C.navy, border: `2px solid ${C.surface}`,
                            borderRadius: "50%", width: 20, height: 20, display: "flex", alignItems: "center", justifyContent: "center", color: "#fff",
                          }}><X size={11} /></button>
                          <div style={{ fontSize: 9.5, color: C.muted, marginTop: 3, whiteSpace: "nowrap", overflow: "hidden", textOverflow: "ellipsis" }}>{f.name}</div>
                        </div>
                      );
                    })}
                  </div>
                )}
              </Field>
            )}

            {task?.evidence === "None" && (
              <div style={{ background: C.greenDim, border: `1px solid ${C.border}`, borderRadius: 8, padding: "10px 12px", fontSize: 12.5, color: C.muted, marginBottom: 14 }}>
                No evidence is required for this task.
              </div>
            )}
          </>
        )}
        <Field label="Comment (optional)">
          <textarea value={comment} onChange={(e) => setComment(e.target.value)} rows={3}
            placeholder="Add context for the reviewer"
            style={{ ...selectStyle, resize: "vertical", fontFamily: fontBody, padding: "9px 12px" }} />
        </Field>
        {error && <div style={{ color: C.red, fontSize: 12.5, marginBottom: 12 }}>{error}</div>}
        {task?.scoringMode !== "attendance" && (
          <button onClick={handleSubmit} style={{
            background: C.blue, border: "none", color: "#fff", fontWeight: 600, fontSize: 13.5,
            padding: "10px 18px", borderRadius: 999, width: "100%",
          }}>
            Submit for review
          </button>
        )}
      </Panel>
    </div>
  );
}

// ---------------- My activity ----------------
function MyActivityView({ submissions, awards, challenges, myPoints, cycleLabel, onResubmit }) {
  const mine = submissions.filter((s) => s.claimant === CURRENT_USER.name || s.beneficiaries.includes(CURRENT_USER.name));
  const myAwards = awards.filter((a) => a.member === CURRENT_USER.name);
  const [resubmittingId, setResubmittingId] = useState(null);
  const [resubmitText, setResubmitText] = useState("");

  const items = [
    ...mine.map((s) => {
      const challenge = challenges.find((c) => c.id === s.challengeId);
      const task = challenge?.tasks.find((t) => t.id === s.taskId);
      return { key: s.id, kind: "submission", raw: s, label: task?.name || "Task", sub: challenge?.name, status: s.status, xp: s.xp, when: s.submittedAt };
    }),
    ...myAwards.map((a) => ({ key: a.id, kind: "award", label: a.reason, sub: categoryLabel(a.categoryCode), status: "Approved", xp: a.xp, when: a.awardedAt })),
  ];

  return (
    <div>
      <SectionTitle eyebrow={cycleLabel ? `${cycleLabel.toUpperCase()} · PERSONAL LEDGER` : "PERSONAL LEDGER"} title="My activity" />
      <div className="pq-grid-3" style={{ marginBottom: 22 }}>
        <StatCard label="XP this cycle" value={myPoints} icon={Flame} tone="gold" />
        <StatCard label="Submissions" value={mine.length} icon={UploadCloud} tone="blue" />
        <StatCard label="Bonus awards" value={myAwards.length} icon={Sparkles} tone="purple" />
      </div>
      <Panel accent={C.blue}>
        <div style={{ fontFamily: fontDisplay, fontWeight: 700, fontSize: 15, marginBottom: 14, color: C.navy }}>Where your XP came from</div>
        {items.length === 0 && <div style={{ color: C.muted, fontSize: 13 }}>Nothing recorded yet this cycle — submit your first task to start earning XP.</div>}
        {items.map((it, i) => {
          const st = statusStyle(it.status);
          const StatusIcon = st.icon;
          const needsAction = it.kind === "submission" && it.status === "Needs Evidence";
          return (
            <div key={it.key} style={{ padding: "11px 0", borderBottom: i < items.length - 1 ? `1px solid ${C.border}` : "none" }}>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center" }}>
                <div style={{ display: "flex", alignItems: "center", gap: 12 }}>
                  <div style={{ width: 30, height: 30, borderRadius: 8, flexShrink: 0, display: "flex", alignItems: "center", justifyContent: "center", background: it.kind === "award" ? C.goldDim : C.blueDim }}>
                    {it.kind === "award" ? <Sparkles size={14} style={{ color: "#8A6416" }} /> : <UploadCloud size={14} style={{ color: C.blue }} />}
                  </div>
                  <div>
                    <div style={{ fontSize: 13.5, fontWeight: 500, color: C.navy }}>{it.label}</div>
                    <div style={{ fontSize: 11.5, color: C.muted, marginTop: 2 }}>{it.sub} · {it.when}</div>
                    {it.kind === "submission" && it.raw.reviewerComment && (
                      <div style={{ fontSize: 11.5, color: "#9A4A15", marginTop: 4, display: "flex", alignItems: "center", gap: 5 }}>
                        <MessageSquare size={11} /> {it.raw.reviewerComment}
                      </div>
                    )}
                  </div>
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                  <div style={{ display: "flex", alignItems: "center", gap: 5, color: st.color, fontSize: 12, fontWeight: 600 }}>
                    <StatusIcon size={13} /> {it.status}
                  </div>
                  <Badge tone={it.status === "Rejected" ? "red" : "gold"}>{it.status === "Rejected" || it.xp === 0 ? "0 XP" : `+${it.xp} XP`}</Badge>
                </div>
              </div>
              {needsAction && (
                <div style={{ marginTop: 10, marginLeft: 42 }}>
                  {resubmittingId === it.key ? (
                    <div>
                      <textarea value={resubmitText} onChange={(e) => setResubmitText(e.target.value)} rows={2} placeholder="Describe the additional evidence you're adding"
                        style={{ ...selectStyle, resize: "vertical", fontFamily: fontBody, marginBottom: 6 }} />
                      <div style={{ display: "flex", gap: 8 }}>
                        <button onClick={() => { onResubmit(it.raw.id, resubmitText); setResubmittingId(null); setResubmitText(""); }} style={{ background: C.blue, border: "none", color: "#fff", fontSize: 12, fontWeight: 600, padding: "6px 12px", borderRadius: 999 }}>
                          Resubmit
                        </button>
                        <button onClick={() => setResubmittingId(null)} style={{ background: "none", border: `1px solid ${C.border}`, color: C.muted, fontSize: 12, padding: "6px 12px", borderRadius: 999 }}>Cancel</button>
                      </div>
                    </div>
                  ) : (
                    <button onClick={() => setResubmittingId(it.key)} style={{ background: C.orangeDim, border: `1px solid #F0C4A0`, color: "#9A4A15", fontSize: 12, fontWeight: 600, padding: "6px 12px", borderRadius: 999, display: "flex", alignItems: "center", gap: 6 }}>
                      <RotateCcw size={12} /> Add evidence & resubmit
                    </button>
                  )}
                </div>
              )}
            </div>
          );
        })}
      </Panel>
    </div>
  );
}

// ---------------- Team ----------------
function TeamView({ teams, myTeam, setTeams, showToast, cycleLabel, isCurrentCycle }) {
  const [showCreate, setShowCreate] = useState(false);
  const [newName, setNewName] = useState("");

  const createTeam = () => {
    if (!newName.trim()) return;
    setTeams((prev) => [...prev, { id: `team${prev.length + 1}`, cycleId: CURRENT_CYCLE, name: newName.trim(), members: [CURRENT_USER.name] }]);
    setNewName(""); setShowCreate(false);
    showToast(`Team "${newName.trim()}" created for ${cycleLabel}`);
  };

  return (
    <div>
      <SectionTitle
        eyebrow={cycleLabel ? `${cycleLabel.toUpperCase()} ROSTER` : "ROSTER"}
        title="My team"
        action={
          isCurrentCycle && (
            <button onClick={() => setShowCreate((v) => !v)} style={{ background: C.surface, border: `1px solid ${C.border}`, color: C.blue, fontSize: 12.5, padding: "7px 12px", borderRadius: 999, display: "flex", alignItems: "center", gap: 5, fontWeight: 600 }}>
              <Plus size={13} /> New team
            </button>
          )
        }
      />
      <div style={{ background: C.blueDim, border: `1px solid ${C.border}`, borderRadius: 8, padding: "9px 14px", fontSize: 12, color: C.muted, marginBottom: 12 }}>
        {isCurrentCycle ? "Teams reset every cycle — form or join a new one each month." : `You're viewing ${cycleLabel}'s roster as read-only history.`}
      </div>
      <div style={{ background: C.purpleDim, border: `1px solid ${C.border}`, borderRadius: 8, padding: "9px 14px", fontSize: 12, color: C.muted, marginBottom: 18 }}>
        Note: some challenges pair people from different teams together (e.g. a challenge requiring pairs when your normal team is a trio). Check each challenge's own rules on the Challenges page — your team here is your default, not a hard limit.
      </div>
      {showCreate && isCurrentCycle && (
        <Panel style={{ marginBottom: 16, maxWidth: 420 }} accent={C.blue}>
          <Field label="Team name">
            <input value={newName} onChange={(e) => setNewName(e.target.value)} placeholder="e.g. AI Trailblazers" style={selectStyle} />
          </Field>
          <button onClick={createTeam} style={{ background: C.blue, border: "none", color: "#fff", fontWeight: 600, fontSize: 13, padding: "8px 16px", borderRadius: 999 }}>Create team</button>
        </Panel>
      )}
      {myTeam ? (
        <Panel style={{ marginBottom: 18 }} accent={C.gold}>
          <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 4 }}>
            <div style={{ fontFamily: fontDisplay, fontWeight: 700, fontSize: 16, color: C.navy }}>{myTeam.name}</div>
            <Badge tone="muted">Team scoring pending</Badge>
          </div>
          <div style={{ fontSize: 12, color: C.muted, marginBottom: 14 }}>This cycle's roster</div>
          <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
            {myTeam.members.map((m) => (
              <div key={m} style={{ display: "flex", alignItems: "center", gap: 8, background: C.surfaceMuted, border: `1px solid ${C.border}`, borderRadius: 999, padding: "6px 12px 6px 6px" }}>
                <div style={{ width: 22, height: 22, borderRadius: "50%", background: C.blue, display: "flex", alignItems: "center", justifyContent: "center", fontSize: 10, fontWeight: 700, color: "#fff" }}>
                  {m.split(" ").map((p) => p[0]).join("")}
                </div>
                <span style={{ fontSize: 12.5, color: C.navy }}>{m}</span>
              </div>
            ))}
          </div>
        </Panel>
      ) : (
        <Panel style={{ marginBottom: 18 }}>
          <div style={{ color: C.muted, fontSize: 13 }}>
            {isCurrentCycle ? `You haven't joined or created a team for ${cycleLabel} yet.` : `You weren't on a team in ${cycleLabel}.`}
          </div>
        </Panel>
      )}
      <div style={{ fontSize: 12, color: C.muted, marginBottom: 10 }}>All teams this cycle</div>
      <div className="pq-grid-3">
        {teams.map((t) => (
          <Panel key={t.id} style={{ padding: "14px 16px" }} accent={C.blue}>
            <div style={{ fontWeight: 600, fontSize: 13.5, marginBottom: 4, color: C.navy }}>{t.name}</div>
            <div style={{ fontSize: 11.5, color: C.muted }}>{t.members.length} members</div>
          </Panel>
        ))}
      </div>
    </div>
  );
}

// ---------------- Leaderboard ----------------
function LeaderboardView({ memberPoints, teams }) {
  const [view, setView] = useState("individual");
  const sortedMembers = Object.entries(memberPoints).sort((a, b) => b[1] - a[1]);

  return (
    <div>
      <SectionTitle
        eyebrow="THIS CYCLE"
        title="Leaderboard"
        action={
          <div style={{ display: "flex", gap: 4, background: C.surfaceMuted, borderRadius: 999, padding: 3, border: `1px solid ${C.border}` }}>
            {["individual", "team"].map((v) => (
              <button key={v} onClick={() => setView(v)} style={{
                border: "none", borderRadius: 999, padding: "6px 14px", fontSize: 12,
                fontWeight: 600, background: view === v ? C.blue : "transparent",
                color: view === v ? "#fff" : C.muted,
              }}>{v === "individual" ? "Individual" : "Team"}</button>
            ))}
          </div>
        }
      />
      {view === "individual" ? (
        <Panel accent={C.gold}>
          {sortedMembers.length === 0 && <div style={{ color: C.muted, fontSize: 13 }}>No participants in this cycle.</div>}
          {sortedMembers.map(([name, pts], i) => (
            <div key={name} style={{ display: "flex", alignItems: "center", gap: 14, padding: "11px 0", borderBottom: i < sortedMembers.length - 1 ? `1px solid ${C.border}` : "none" }}>
              <div style={{
                width: 26, height: 26, borderRadius: "50%", display: "flex", alignItems: "center", justifyContent: "center",
                fontFamily: fontMono, fontSize: 12, fontWeight: 700,
                background: pts === 0 ? C.surfaceMuted : i === 0 ? C.gold : i === 1 ? C.border : i === 2 ? "#EADFB8" : C.surfaceMuted,
                color: C.navy,
              }}>{i + 1}</div>
              <div style={{ flex: 1, fontSize: 13.5, fontWeight: 500, color: pts === 0 ? C.muted : C.navy }}>{name}</div>
              <span style={{ fontFamily: fontMono, fontSize: 13, color: pts === 0 ? C.muted : "#8A6416", fontWeight: 700 }}>{pts} XP</span>
            </div>
          ))}
        </Panel>
      ) : (
        <div>
          <div style={{
            background: C.orangeDim, border: `1px solid #F0C4A0`, borderRadius: 8, padding: "12px 16px",
            fontSize: 13, color: "#9A4A15", marginBottom: 16, display: "flex", alignItems: "flex-start", gap: 10,
          }}>
            <AlertTriangle size={16} style={{ flexShrink: 0, marginTop: 1 }} />
            <div>
              <div style={{ fontWeight: 700, marginBottom: 3 }}>Scoring rule pending confirmation</div>
              Preety hasn't yet confirmed how team XP is calculated — whether a shared task contributes the sum of members' XP, one flat completion score, or a separate team score entirely. Team totals will appear here once that's decided (see spec §10).
            </div>
          </div>
          <Panel>
            {teams.length === 0 && <div style={{ color: C.muted, fontSize: 13 }}>No teams in this cycle.</div>}
            {teams.map((t, i) => (
              <div key={t.id} style={{ display: "flex", alignItems: "center", justifyContent: "space-between", padding: "11px 0", borderBottom: i < teams.length - 1 ? `1px solid ${C.border}` : "none" }}>
                <div style={{ fontSize: 13.5, fontWeight: 500, color: C.navy }}>{t.name}</div>
                <Badge tone="muted">Pending</Badge>
              </div>
            ))}
          </Panel>
        </div>
      )}
    </div>
  );
}

// ---------------- Review Queue ----------------
function ReviewView({ submissions, challenges, onApprove, onReject, onRequestEvidence, awards, onAwardXP, rosterHint, awardCycleId, awardCycleLabel }) {
  const pending = submissions.filter((s) => s.status === "Under Review" || s.status === "Resubmitted");
  const resolved = submissions.filter((s) => !["Under Review", "Resubmitted"].includes(s.status));
  const [showAward, setShowAward] = useState(false);
  const [member, setMember] = useState("");
  const [categoryCode, setCategoryCode] = useState(AWARD_CATEGORIES[0].code);
  const [reason, setReason] = useState("");
  const [xp, setXp] = useState("");
  const [error, setError] = useState("");

  const submitAward = () => {
    if (!awardCycleId) {
      setError("Choose a specific reporting cycle before awarding XP.");
      return;
    }
    if (!member.trim() || !reason.trim() || !xp || Number(xp) <= 0) {
      setError("Enter a participant, reason, and a positive XP amount.");
      return;
    }
    setError("");
    onAwardXP({ member: member.trim(), categoryCode, reason: reason.trim(), xp: Number(xp), cycleId: awardCycleId });
    setMember(""); setReason(""); setXp(""); setShowAward(false);
  };

  return (
    <div>
      <SectionTitle
        eyebrow={`${pending.length} PENDING`}
        title="Review queue"
        action={
          <button onClick={() => setShowAward((v) => !v)} style={{ background: C.surface, border: `1px solid ${C.border}`, color: C.blue, fontSize: 12.5, padding: "7px 12px", borderRadius: 999, display: "flex", alignItems: "center", gap: 5, fontWeight: 600 }}>
            <Plus size={13} /> Award bonus XP
          </button>
        }
      />
      <div style={{ background: C.goldDim, border: `1px solid #F0D98A`, borderRadius: 8, padding: "9px 14px", fontSize: 12, color: "#8A6416", marginBottom: 18 }}>
        Use this for XP that isn't tied to a file submission — raid participation, early-bird or buddy bonuses, Friday Funny votes, birthday shout-outs.
      </div>
      {showAward && (
        <Panel style={{ marginBottom: 20, maxWidth: 480 }} accent={C.gold}>
          <Field label="Reporting cycle">
            <div style={{ ...selectStyle, color: awardCycleId ? C.navy : C.red, background: awardCycleId ? C.surfaceMuted : C.redDim }}>
              {awardCycleLabel || "Choose a specific cycle from the cycle selector above"}
            </div>
          </Field>
          <Field label="Participant">
            <input value={member} onChange={(e) => setMember(e.target.value)} placeholder="e.g. Angela Kaur" list="roster" style={selectStyle} />
            <datalist id="roster">{rosterHint?.map((n) => <option key={n} value={n} />)}</datalist>
          </Field>
          <Field label="Award category">
            <select value={categoryCode} onChange={(e) => setCategoryCode(e.target.value)} style={selectStyle}>
              {AWARD_CATEGORIES.map((c) => <option key={c.code} value={c.code}>{c.label}</option>)}
            </select>
          </Field>
          <Field label="Reason / detail">
            <input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. Remote raid, Lobby 2 participation" style={selectStyle} />
          </Field>
          <Field label="XP amount">
            <input type="number" min="1" value={xp} onChange={(e) => setXp(e.target.value)} placeholder="15" style={selectStyle} />
          </Field>
          <div style={{ fontSize: 11, color: C.muted, marginBottom: 12 }}>No team field here on purpose — team scoring is still pending confirmation (§10), so a bonus award can't silently decide it.</div>
          {error && <div style={{ color: C.red, fontSize: 12.5, marginBottom: 12 }}>{error}</div>}
          <button onClick={submitAward} style={{ background: C.gold, border: "none", color: "#4A3600", fontWeight: 700, fontSize: 13, padding: "8px 16px", borderRadius: 999 }}>
            Award XP
          </button>
        </Panel>
      )}
      {pending.length === 0 && (
        <Panel style={{ marginBottom: 20 }}><div style={{ color: C.muted, fontSize: 13 }}>All caught up — nothing waiting on you.</div></Panel>
      )}
      <div style={{ display: "flex", flexDirection: "column", gap: 10, marginBottom: 26 }}>
        {pending.map((s) => (
          <SubmissionRow key={s.id} s={s} challenges={challenges} onApprove={onApprove} onReject={onReject} onRequestEvidence={onRequestEvidence} />
        ))}
      </div>
      {awards.length > 0 && (
        <>
          <div style={{ fontSize: 12, color: C.muted, marginBottom: 10 }}>Recent bonus awards</div>
          <Panel style={{ marginBottom: 26 }} accent={C.gold}>
            {awards.map((a, i) => (
              <div key={a.id} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "9px 0", borderBottom: i < awards.length - 1 ? `1px solid ${C.border}` : "none" }}>
                <div>
                  <div style={{ fontSize: 13, fontWeight: 500, color: C.navy }}>{a.member}</div>
                  <div style={{ fontSize: 11.5, color: C.muted, marginTop: 2 }}>{categoryLabel(a.categoryCode)} · {a.reason} · {a.awardedAt}</div>
                </div>
                <Badge tone="gold">+{a.xp} XP</Badge>
              </div>
            ))}
          </Panel>
        </>
      )}
      {resolved.length > 0 && (
        <>
          <div style={{ fontSize: 12, color: C.muted, marginBottom: 10 }}>Recently resolved</div>
          <div style={{ display: "flex", flexDirection: "column", gap: 8 }}>
            {resolved.map((s) => (
              <SubmissionRow key={s.id} s={s} challenges={challenges} compact />
            ))}
          </div>
        </>
      )}
    </div>
  );
}

function SubmissionRow({ s, challenges, onApprove, onReject, onRequestEvidence, compact }) {
  const challenge = challenges.find((c) => c.id === s.challengeId);
  const task = challenge?.tasks.find((t) => t.id === s.taskId);
  const Icon = evidenceIcon(s.fileType);
  const st = statusStyle(s.status);
  const StatusIcon = st.icon;
  const cat = challenge ? categoryColor(challenge.category) : { fg: C.blue, bg: C.blueDim };
  const [showRequestBox, setShowRequestBox] = useState(false);
  const [requestComment, setRequestComment] = useState("");

  const showBeneficiaries = s.beneficiaries && (s.beneficiaries.length > 1 || s.beneficiaries[0] !== s.claimant);

  return (
    <Panel style={{ padding: compact ? "12px 16px" : "14px 18px" }} accent={cat.fg}>
      <div style={{ display: "flex", alignItems: "flex-start", gap: 14 }}>
        <div style={{ width: 34, height: 34, borderRadius: 8, background: cat.bg, display: "flex", alignItems: "center", justifyContent: "center", flexShrink: 0 }}>
          <Icon size={16} style={{ color: cat.fg }} />
        </div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 13.5, fontWeight: 600, color: C.navy }}>{task?.name || "Task"}</div>
          <div style={{ fontSize: 11.5, color: C.muted, marginTop: 2 }}>
            {challenge?.name} · {s.team} · submitted by {s.claimant} · {s.submittedAt}
          </div>
          {showBeneficiaries && (
            <div style={{ fontSize: 11.5, color: C.blue, marginTop: 3, fontWeight: 600 }}>For: {s.beneficiaries.join(", ")}</div>
          )}
          {s.textResponse && <div style={{ fontSize: 12, color: C.muted, marginTop: 6 }}><strong>Text:</strong> {s.textResponse}</div>}
          {s.links?.length > 0 && <div style={{ fontSize: 12, color: C.blue, marginTop: 4 }}><strong>Link:</strong> {s.links.join(", ")}</div>}
          {s.comment && <div style={{ fontSize: 12, color: C.muted, marginTop: 6, fontStyle: "italic" }}>"{s.comment}"</div>}
          {s.reviewerComment && (
            <div style={{ fontSize: 12, color: "#9A4A15", marginTop: 6, display: "flex", alignItems: "center", gap: 5 }}>
              <MessageSquare size={11} /> {s.reviewerComment}
            </div>
          )}
          {s.fileName && <div style={{ fontSize: 11, color: C.muted, marginTop: 6, fontFamily: fontMono }}>{s.fileName}</div>}
        </div>
        <div style={{ display: "flex", flexDirection: "column", alignItems: "flex-end", gap: 8, flexShrink: 0 }}>
          <div style={{ display: "flex", alignItems: "center", gap: 5, color: st.color, fontSize: 12, fontWeight: 600 }}>
            <StatusIcon size={13} /> {s.status}
          </div>
          {!compact && (
            <div style={{ display: "flex", gap: 8 }}>
              <button onClick={() => onReject(s.id)} style={{ background: C.surface, border: `1px solid ${C.border}`, color: C.red, fontSize: 12, padding: "6px 12px", borderRadius: 999, fontWeight: 600 }}>
                Reject
              </button>
              <button onClick={() => setShowRequestBox((v) => !v)} style={{ background: C.orangeDim, border: `1px solid #F0C4A0`, color: "#9A4A15", fontSize: 12, padding: "6px 12px", borderRadius: 999, fontWeight: 600 }}>
                Needs evidence
              </button>
              <button onClick={() => onApprove(s.id, task?.xp || 0)} style={{ background: C.green, border: "none", color: "#fff", fontSize: 12, padding: "6px 12px", borderRadius: 999, fontWeight: 700 }}>
                Approve +{task?.xp || 0} XP{showBeneficiaries ? " each" : ""}
              </button>
            </div>
          )}
        </div>
      </div>
      {showRequestBox && (
        <div style={{ marginTop: 12, paddingTop: 12, borderTop: `1px solid ${C.border}` }}>
          <textarea value={requestComment} onChange={(e) => setRequestComment(e.target.value)} rows={2}
            placeholder="What's missing? e.g. Please show enrolment proof for all 3 members."
            style={{ ...selectStyle, resize: "vertical", fontFamily: fontBody, marginBottom: 8 }} />
          <button onClick={() => { onRequestEvidence(s.id, requestComment); setShowRequestBox(false); setRequestComment(""); }} style={{ background: C.orange, border: "none", color: "#fff", fontSize: 12, fontWeight: 600, padding: "6px 12px", borderRadius: 999 }}>
            Send request
          </button>
        </div>
      )}
    </Panel>
  );
}

// ---------------- Scoresheet ----------------
function ScoresheetView({ challenges, roster, submissions, awards, cycleLabel, raidPasses }) {
  const [sortDesc, setSortDesc] = useState(true);

  const categoriesPresent = useMemo(
    () => [...new Set(awards.map((a) => a.categoryCode))],
    [awards]
  );

  const taskColumns = useMemo(
    () => challenges.flatMap((challenge) =>
      challenge.tasks.map((task, index) => ({
        challengeId: challenge.id,
        taskId: task.id,
        label: `${challenge.category}${challenge.eyebrow.match(/\d+/) ? ` ${challenge.eyebrow.match(/\d+/)[0]}` : ""} T${index + 1}`,
        title: `${challenge.name} — ${task.name}`,
      }))
    ),
    [challenges]
  );

  const rows = useMemo(() => {
    return roster.map((name) => {
      const perTask = {};
      taskColumns.forEach((col) => {
        perTask[col.taskId] = submissions
          .filter((s) =>
            s.status === "Approved" &&
            s.challengeId === col.challengeId &&
            s.taskId === col.taskId &&
            s.beneficiaries.includes(name)
          )
          .reduce((sum, s) => sum + s.xp, 0);
      });

      const perCategory = {};
      categoriesPresent.forEach((code) => {
        perCategory[code] = awards
          .filter((a) => a.member === name && a.categoryCode === code)
          .reduce((sum, a) => sum + a.xp, 0);
      });

      const total =
        Object.values(perTask).reduce((a, b) => a + b, 0) +
        Object.values(perCategory).reduce((a, b) => a + b, 0);

      return { name, perTask, perCategory, total };
    }).sort((a, b) => sortDesc ? b.total - a.total : a.total - b.total);
  }, [roster, taskColumns, submissions, awards, categoriesPresent, sortDesc]);

  return (
    <div>
      <SectionTitle eyebrow={cycleLabel ? `${cycleLabel.toUpperCase()} · REPLACES THE CSV` : "REPLACES THE CSV"} title="Scoresheet" />
      <Panel accent={C.blue} style={{ padding: 0, overflow: "auto" }}>
        <table style={{ width: "100%", minWidth: 760, borderCollapse: "collapse", fontSize: 13 }}>
          <thead>
            <tr style={{ background: C.surfaceMuted }}>
              <th style={thStyle}>Participant</th>
              {taskColumns.map((col) => <th key={col.taskId} title={col.title} style={thStyle}>{col.label}</th>)}
              {categoriesPresent.map((code) => <th key={code} style={thStyle}>{categoryLabel(code)}</th>)}
              <th style={{ ...thStyle, cursor: "pointer", color: C.blue }} onClick={() => setSortDesc((v) => !v)}>
                Total {sortDesc ? "↓" : "↑"}
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map((r, i) => (
              <tr key={r.name} style={{ background: i % 2 === 0 ? C.surface : C.surfaceMuted }}>
                <td style={{ ...tdStyle, fontWeight: 500, color: C.navy }}>{r.name}</td>
                {taskColumns.map((col) => (
                  <td key={col.taskId} style={{ ...tdStyle, fontFamily: fontMono, color: r.perTask[col.taskId] ? C.navy : C.border }}>
                    {r.perTask[col.taskId] || "—"}
                  </td>
                ))}
                {categoriesPresent.map((code) => (
                  <td key={code} style={{ ...tdStyle, fontFamily: fontMono, color: r.perCategory[code] ? "#8A6416" : C.border }}>
                    {r.perCategory[code] || "—"}
                  </td>
                ))}
                <td style={{ ...tdStyle, fontFamily: fontMono, fontWeight: 700, color: C.navy }}>{r.total}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </Panel>
      <div style={{ fontSize: 11.5, color: C.muted, marginTop: 10, marginBottom: 22 }}>
        Every task has its own column, followed by configurable manual-award categories. Every cell traces back to an approved submission or award; nothing is typed in directly. Includes zero-XP participants from the explicit mock cycle roster, not just people with activity.
      </div>

      {raidPasses.length > 0 && (
        <div>
          <div style={{ fontSize: 12, color: C.muted, marginBottom: 10 }}>Raid passes — tracked separately, not included in XP totals</div>
          <Panel accent={C.teal} style={{ padding: 0, overflow: "auto" }}>
            <table style={{ width: "100%", minWidth: 480, borderCollapse: "collapse", fontSize: 13 }}>
              <thead>
                <tr style={{ background: C.surfaceMuted }}>
                  <th style={thStyle}>Participant</th>
                  <th style={thStyle}>Physical assigned</th>
                  <th style={thStyle}>Physical used</th>
                  <th style={thStyle}>Remote assigned</th>
                  <th style={thStyle}>Remote used</th>
                </tr>
              </thead>
              <tbody>
                {raidPasses.map((p, i) => (
                  <tr key={p.name} style={{ background: i % 2 === 0 ? C.surface : C.surfaceMuted }}>
                    <td style={{ ...tdStyle, fontWeight: 500, color: C.navy }}>{p.name}</td>
                    <td style={{ ...tdStyle, fontFamily: fontMono }}>{p.physicalAssigned}</td>
                    <td style={{ ...tdStyle, fontFamily: fontMono }}>{p.physicalUsed}</td>
                    <td style={{ ...tdStyle, fontFamily: fontMono }}>{p.remoteAssigned}</td>
                    <td style={{ ...tdStyle, fontFamily: fontMono }}>{p.remoteUsed}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </Panel>
        </div>
      )}
    </div>
  );
}

const thStyle = {
  textAlign: "left", padding: "10px 14px", fontSize: 11, fontWeight: 700, color: C.muted,
  borderBottom: `1px solid ${C.border}`, whiteSpace: "nowrap", textTransform: "uppercase", letterSpacing: "0.03em",
};
const tdStyle = { padding: "9px 14px", borderBottom: `1px solid ${C.border}`, whiteSpace: "nowrap" };

// ---------------- Analytics ----------------
function AnalyticsView({ submissions, teams, challenges, cycles }) {
  const total = submissions.length;
  const approved = submissions.filter((s) => s.status === "Approved").length;
  const rate = total ? Math.round((approved / total) * 100) : 0;
  const activeParticipants = new Set(submissions.flatMap((s) => s.beneficiaries)).size;

  return (
    <div>
      <SectionTitle eyebrow="FOR LEADERSHIP · ALL CYCLES" title="Analytics" />
      <div className="pq-grid-4" style={{ marginBottom: 22 }}>
        <StatCard label="Total participants" value={activeParticipants} icon={Users} tone="blue" />
        <StatCard label="Total submissions" value={total} icon={UploadCloud} tone="purple" />
        <StatCard label="Approval rate" value={`${rate}%`} icon={ShieldCheck} tone="green" />
        <StatCard label="Cycles tracked" value={cycles.length} icon={History} tone="gold" />
      </div>
      <Panel style={{ marginBottom: 18 }} accent={C.blue}>
        <div style={{ fontFamily: fontDisplay, fontWeight: 700, fontSize: 15, marginBottom: 16, color: C.navy }}>Submissions by week, across cycles</div>
        <div style={{ height: 220 }}>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={weeklyParticipation}>
              <CartesianGrid strokeDasharray="3 3" stroke={C.border} vertical={false} />
              <XAxis dataKey="week" tick={{ fill: C.muted, fontSize: 11 }} axisLine={{ stroke: C.border }} tickLine={false} />
              <YAxis tick={{ fill: C.muted, fontSize: 12 }} axisLine={false} tickLine={false} />
              <Tooltip contentStyle={{ background: "#fff", border: `1px solid ${C.border}`, borderRadius: 8, fontSize: 12 }} labelStyle={{ color: C.navy }} />
              <Bar dataKey="submissions" fill={C.blue} radius={[5, 5, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      </Panel>
      <Panel accent={C.purple}>
        <div style={{ fontFamily: fontDisplay, fontWeight: 700, fontSize: 15, marginBottom: 14, color: C.navy }}>Cycles at a glance</div>
        {cycles.map((cy) => {
          const cyChallenges = challenges.filter((c) => c.cycleId === cy.id);
          const cyTeams = teams.filter((t) => t.cycleId === cy.id);
          const cyIds = new Set(cyChallenges.map((c) => c.id));
          const cySubs = submissions.filter((s) => cyIds.has(s.challengeId));
          return (
            <div key={cy.id} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", padding: "10px 0", borderBottom: `1px solid ${C.border}`, fontSize: 13 }}>
              <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
                <span style={{ fontWeight: 500, color: C.navy }}>{cy.label}</span>
                <CycleBadge status={cy.status} />
              </div>
              <span style={{ color: C.muted }}>{cyTeams.length} teams · {cyChallenges.length} challenges · {cySubs.length} submissions</span>
            </div>
          );
        })}
      </Panel>
    </div>
  );
}
