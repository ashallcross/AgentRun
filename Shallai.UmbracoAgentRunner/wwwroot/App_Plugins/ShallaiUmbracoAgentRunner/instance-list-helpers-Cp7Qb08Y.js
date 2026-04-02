function a(e) {
  const n = Math.floor((Date.now() - new Date(e).getTime()) / 1e3);
  if (n < 60) return "just now";
  const t = Math.floor(n / 60);
  if (t < 60) return `${t} minute${t === 1 ? "" : "s"} ago`;
  const s = Math.floor(t / 60);
  if (s < 24) return `${s} hour${s === 1 ? "" : "s"} ago`;
  const o = Math.floor(s / 24);
  if (o < 30) return `${o} day${o === 1 ? "" : "s"} ago`;
  const r = Math.floor(o / 30);
  return `${r} month${r === 1 ? "" : "s"} ago`;
}
function i(e) {
  const n = e.split("/"), t = n[n.length - 1];
  return decodeURIComponent(t);
}
function u(e, n) {
  const t = n.split("/");
  return t.length >= 2 && t.splice(-2, 2, "instances", e), t.join("/");
}
function c(e) {
  const n = e.lastIndexOf("/");
  return n > 0 ? e.substring(0, n) : e;
}
function l(e, n) {
  const t = n !== "autonomous";
  switch (e) {
    case "Running":
      return t ? "In progress" : "Running";
    case "Failed":
      return t ? "In progress" : "Failed";
    case "Completed":
      return "Complete";
    case "Pending":
      return "Pending";
    case "Cancelled":
      return "Cancelled";
    default:
      return e;
  }
}
function d(e, n) {
  const t = n !== "autonomous";
  switch (e) {
    case "Completed":
      return "positive";
    case "Failed":
      return t ? void 0 : "danger";
    case "Running":
      return t ? void 0 : "warning";
    default:
      return;
  }
}
function f(e) {
  return e !== "autonomous" ? {
    newButton: "New session",
    emptyState: "No sessions yet. Start one to begin."
  } : {
    newButton: "New Run",
    emptyState: "No runs yet. Click 'New Run' to start."
  };
}
function g(e) {
  const t = [...e].sort(
    (s, o) => new Date(s.createdAt).getTime() - new Date(o.createdAt).getTime()
  ).map((s, o) => ({ ...s, runNumber: o + 1 }));
  return t.reverse(), t;
}
export {
  c as a,
  u as b,
  l as d,
  i as e,
  f as i,
  g as n,
  a as r,
  d as s
};
//# sourceMappingURL=instance-list-helpers-Cp7Qb08Y.js.map
