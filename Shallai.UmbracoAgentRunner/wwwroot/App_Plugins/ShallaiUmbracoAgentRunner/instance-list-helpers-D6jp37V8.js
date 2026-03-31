function a(t) {
  const n = Math.floor((Date.now() - new Date(t).getTime()) / 1e3);
  if (n < 60) return "just now";
  const e = Math.floor(n / 60);
  if (e < 60) return `${e} minute${e === 1 ? "" : "s"} ago`;
  const r = Math.floor(e / 60);
  if (r < 24) return `${r} hour${r === 1 ? "" : "s"} ago`;
  const s = Math.floor(r / 24);
  if (s < 30) return `${s} day${s === 1 ? "" : "s"} ago`;
  const o = Math.floor(s / 30);
  return `${o} month${o === 1 ? "" : "s"} ago`;
}
function i(t) {
  const n = t.split("/"), e = n[n.length - 1];
  return decodeURIComponent(e);
}
function u(t, n) {
  const e = n.split("/");
  return e.length >= 2 && e.splice(-2, 2, "instances", t), e.join("/");
}
function c(t) {
  const n = t.lastIndexOf("/");
  return n > 0 ? t.substring(0, n) : t;
}
function l(t) {
  switch (t) {
    case "Completed":
      return "positive";
    case "Failed":
      return "danger";
    case "Running":
      return "warning";
    default:
      return;
  }
}
function d(t) {
  return t === "Completed" || t === "Failed" || t === "Cancelled";
}
function f(t) {
  const e = [...t].sort(
    (r, s) => new Date(r.createdAt).getTime() - new Date(s.createdAt).getTime()
  ).map((r, s) => ({ ...r, runNumber: s + 1 }));
  return e.reverse(), e;
}
export {
  c as a,
  u as b,
  i as e,
  d as i,
  f as n,
  a as r,
  l as s
};
//# sourceMappingURL=instance-list-helpers-D6jp37V8.js.map
