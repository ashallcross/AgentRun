const r = "/umbraco/api/shallai";
async function a(t, n) {
  const o = { Accept: "application/json" };
  n && (o.Authorization = `Bearer ${n}`);
  const e = await fetch(`${r}${t}`, { headers: o });
  if (!e.ok)
    throw new Error(`API error: ${e.status} ${e.statusText}`);
  return e.json();
}
async function c(t, n, o) {
  const e = {
    Accept: "application/json",
    "Content-Type": "application/json"
  };
  o && (e.Authorization = `Bearer ${o}`);
  const s = await fetch(`${r}${t}`, {
    method: "POST",
    headers: e,
    body: JSON.stringify(n)
  });
  if (!s.ok)
    throw new Error(`API error: ${s.status} ${s.statusText}`);
  return s.json();
}
async function i(t, n) {
  const o = {};
  n && (o.Authorization = `Bearer ${n}`);
  const e = await fetch(`${r}${t}`, {
    method: "DELETE",
    headers: o
  });
  if (!e.ok)
    throw new Error(`API error: ${e.status} ${e.statusText}`);
}
function u(t) {
  return a("/workflows", t);
}
function f(t, n) {
  return a(`/instances?workflowAlias=${encodeURIComponent(t)}`, n);
}
function p(t, n) {
  return c("/instances", { workflowAlias: t }, n);
}
function $(t, n) {
  return i(`/instances/${encodeURIComponent(t)}`, n);
}
export {
  f as a,
  p as c,
  $ as d,
  u as g
};
//# sourceMappingURL=api-client-DNQcFMx0.js.map
