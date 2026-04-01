const a = "/umbraco/api/shallai";
async function r(t, n) {
  const e = { Accept: "application/json" };
  n && (e.Authorization = `Bearer ${n}`);
  const o = await fetch(`${a}${t}`, { headers: e });
  if (!o.ok)
    throw new Error(`API error: ${o.status} ${o.statusText}`);
  return o.json();
}
async function c(t, n, e) {
  const o = {
    Accept: "application/json",
    "Content-Type": "application/json"
  };
  e && (o.Authorization = `Bearer ${e}`);
  const s = await fetch(`${a}${t}`, {
    method: "POST",
    headers: o,
    body: JSON.stringify(n)
  });
  if (!s.ok)
    throw new Error(`API error: ${s.status} ${s.statusText}`);
  return s.json();
}
async function i(t, n) {
  const e = {};
  n && (e.Authorization = `Bearer ${n}`);
  const o = await fetch(`${a}${t}`, {
    method: "DELETE",
    headers: e
  });
  if (!o.ok)
    throw new Error(`API error: ${o.status} ${o.statusText}`);
}
function u(t) {
  return r("/workflows", t);
}
function f(t, n) {
  return r(`/instances?workflowAlias=${encodeURIComponent(t)}`, n);
}
function p(t, n) {
  return c("/instances", { workflowAlias: t }, n);
}
function l(t, n) {
  return i(`/instances/${encodeURIComponent(t)}`, n);
}
function m(t, n) {
  return r(`/instances/${encodeURIComponent(t)}`, n);
}
function $(t, n) {
  return c(`/instances/${encodeURIComponent(t)}/cancel`, {}, n);
}
async function h(t, n) {
  const e = {};
  return n && (e.Authorization = `Bearer ${n}`), fetch(`${a}/instances/${encodeURIComponent(t)}/start`, {
    method: "POST",
    headers: e
  });
}
function d(t, n, e) {
  return r(
    `/instances/${encodeURIComponent(t)}/conversation/${encodeURIComponent(n)}`,
    e
  );
}
function I(t) {
  const n = [];
  for (const e of t)
    e.role === "assistant" && e.content != null && !e.toolCallId ? n.push({
      role: "agent",
      content: e.content,
      timestamp: e.timestamp
    }) : e.role === "system" && e.content != null && n.push({
      role: "system",
      content: e.content,
      timestamp: e.timestamp
    });
  return n;
}
export {
  f as a,
  m as b,
  p as c,
  l as d,
  d as e,
  $ as f,
  u as g,
  I as m,
  h as s
};
//# sourceMappingURL=api-client-BCa3QQsh.js.map
