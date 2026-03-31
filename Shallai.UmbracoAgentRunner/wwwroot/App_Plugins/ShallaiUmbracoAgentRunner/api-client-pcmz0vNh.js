const r = "/umbraco/api/shallai";
async function a(n, t) {
  const o = { Accept: "application/json" };
  t && (o.Authorization = `Bearer ${t}`);
  const e = await fetch(`${r}${n}`, { headers: o });
  if (!e.ok)
    throw new Error(`API error: ${e.status} ${e.statusText}`);
  return e.json();
}
async function c(n, t, o) {
  const e = {
    Accept: "application/json",
    "Content-Type": "application/json"
  };
  o && (e.Authorization = `Bearer ${o}`);
  const s = await fetch(`${r}${n}`, {
    method: "POST",
    headers: e,
    body: JSON.stringify(t)
  });
  if (!s.ok)
    throw new Error(`API error: ${s.status} ${s.statusText}`);
  return s.json();
}
async function i(n, t) {
  const o = {};
  t && (o.Authorization = `Bearer ${t}`);
  const e = await fetch(`${r}${n}`, {
    method: "DELETE",
    headers: o
  });
  if (!e.ok)
    throw new Error(`API error: ${e.status} ${e.statusText}`);
}
function u(n) {
  return a("/workflows", n);
}
function f(n, t) {
  return a(`/instances?workflowAlias=${encodeURIComponent(n)}`, t);
}
function p(n, t) {
  return c("/instances", { workflowAlias: n }, t);
}
function $(n, t) {
  return i(`/instances/${encodeURIComponent(n)}`, t);
}
function h(n, t) {
  return a(`/instances/${encodeURIComponent(n)}`, t);
}
function w(n, t) {
  return c(`/instances/${encodeURIComponent(n)}/cancel`, {}, t);
}
export {
  f as a,
  h as b,
  p as c,
  $ as d,
  w as e,
  u as g
};
//# sourceMappingURL=api-client-pcmz0vNh.js.map
