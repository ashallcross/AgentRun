const r = "/umbraco/api/shallai";
async function a(t, n) {
  const e = { Accept: "application/json" };
  n && (e.Authorization = `Bearer ${n}`);
  const s = await fetch(`${r}${t}`, { headers: e });
  if (!s.ok)
    throw new Error(`API error: ${s.status} ${s.statusText}`);
  return s.json();
}
async function c(t, n, e) {
  const s = {
    Accept: "application/json",
    "Content-Type": "application/json"
  };
  e && (s.Authorization = `Bearer ${e}`);
  const o = await fetch(`${r}${t}`, {
    method: "POST",
    headers: s,
    body: JSON.stringify(n)
  });
  if (!o.ok)
    throw new Error(`API error: ${o.status} ${o.statusText}`);
  return o.json();
}
async function i(t, n) {
  const e = {};
  n && (e.Authorization = `Bearer ${n}`);
  const s = await fetch(`${r}${t}`, {
    method: "DELETE",
    headers: e
  });
  if (!s.ok)
    throw new Error(`API error: ${s.status} ${s.statusText}`);
}
function u(t) {
  return a("/workflows", t);
}
function f(t, n) {
  return a(`/instances?workflowAlias=${encodeURIComponent(t)}`, n);
}
function $(t, n) {
  return c("/instances", { workflowAlias: t }, n);
}
function h(t, n) {
  return i(`/instances/${encodeURIComponent(t)}`, n);
}
function p(t, n) {
  return a(`/instances/${encodeURIComponent(t)}`, n);
}
function d(t, n) {
  return c(`/instances/${encodeURIComponent(t)}/cancel`, {}, n);
}
async function I(t, n) {
  const e = {};
  return n && (e.Authorization = `Bearer ${n}`), fetch(`${r}/instances/${encodeURIComponent(t)}/start`, {
    method: "POST",
    headers: e
  });
}
export {
  f as a,
  p as b,
  $ as c,
  h as d,
  d as e,
  u as g,
  I as s
};
//# sourceMappingURL=api-client-CgFnA5nu.js.map
