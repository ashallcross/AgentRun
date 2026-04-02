function f(e, n) {
  if (!n) return e;
  if (typeof n.path == "string") {
    const t = n.path.split("/");
    return t[t.length - 1] || e;
  }
  if (typeof n.url == "string")
    return n.url.length > 60 ? n.url.slice(0, 60) + "…" : n.url;
  for (const t of Object.values(n))
    if (typeof t == "string") return t;
  return e;
}
const a = "/umbraco/api/shallai";
async function l(e, n) {
  const t = { Accept: "application/json" };
  n && (t.Authorization = `Bearer ${n}`);
  const o = await fetch(`${a}${e}`, { headers: t });
  if (!o.ok)
    throw new Error(`API error: ${o.status} ${o.statusText}`);
  return o.json();
}
async function i(e, n, t) {
  const o = {
    Accept: "application/json",
    "Content-Type": "application/json"
  };
  t && (o.Authorization = `Bearer ${t}`);
  const s = await fetch(`${a}${e}`, {
    method: "POST",
    headers: o,
    body: JSON.stringify(n)
  });
  if (!s.ok)
    throw new Error(`API error: ${s.status} ${s.statusText}`);
  return s.json();
}
function h(e) {
  return l("/workflows", e);
}
function d(e, n) {
  return l(`/instances?workflowAlias=${encodeURIComponent(e)}`, n);
}
function $(e, n) {
  return i("/instances", { workflowAlias: e }, n);
}
function C(e, n) {
  return l(`/instances/${encodeURIComponent(e)}`, n);
}
function I(e, n) {
  return i(`/instances/${encodeURIComponent(e)}/cancel`, {}, n);
}
async function y(e, n, t) {
  const o = { "Content-Type": "application/json" };
  t && (o.Authorization = `Bearer ${t}`);
  const s = await fetch(
    `${a}/instances/${encodeURIComponent(e)}/message`,
    { method: "POST", headers: o, body: JSON.stringify({ message: n }) }
  );
  if (!s.ok)
    throw new Error(`API error: ${s.status} ${s.statusText}`);
}
async function g(e, n) {
  const t = {};
  return n && (t.Authorization = `Bearer ${n}`), fetch(`${a}/instances/${encodeURIComponent(e)}/start`, {
    method: "POST",
    headers: t
  });
}
async function A(e, n) {
  const t = {};
  return n && (t.Authorization = `Bearer ${n}`), fetch(`${a}/instances/${encodeURIComponent(e)}/retry`, {
    method: "POST",
    headers: t
  });
}
function w(e, n, t) {
  return l(
    `/instances/${encodeURIComponent(e)}/conversation/${encodeURIComponent(n)}`,
    t
  );
}
function T(e) {
  const n = [];
  for (const t of e)
    if (t.role === "assistant" && t.content != null && !t.toolCallId)
      n.push({
        role: "agent",
        content: t.content,
        timestamp: t.timestamp
      });
    else if (t.role === "assistant" && t.toolCallId) {
      let o = null;
      if (t.toolArguments)
        try {
          o = JSON.parse(t.toolArguments);
        } catch {
          o = null;
        }
      const s = {
        toolCallId: t.toolCallId,
        toolName: t.toolName ?? "unknown",
        summary: f(t.toolName ?? "unknown", o),
        arguments: o,
        result: null,
        status: "complete"
      }, r = p(n);
      r ? r.toolCalls = [...r.toolCalls ?? [], s] : n.push({
        role: "agent",
        content: "",
        timestamp: t.timestamp,
        toolCalls: [s]
      });
    } else if (t.role === "tool" && t.toolCallId) {
      const o = t.toolResult ?? null, s = typeof o == "string" && o.startsWith("Tool '") && (o.includes("error") || o.includes("failed"));
      for (let r = n.length - 1; r >= 0; r--) {
        const c = n[r].toolCalls?.find((u) => u.toolCallId === t.toolCallId);
        if (c) {
          c.result = o, s && (c.status = "error");
          break;
        }
      }
    } else t.role === "user" && t.content != null ? n.push({
      role: "user",
      content: t.content,
      timestamp: t.timestamp
    }) : t.role === "system" && t.content != null && n.push({
      role: "system",
      content: t.content,
      timestamp: t.timestamp
    });
  return n;
}
function p(e) {
  for (let n = e.length - 1; n >= 0; n--)
    if (e[n].role === "agent") return e[n];
  return null;
}
export {
  d as a,
  C as b,
  $ as c,
  w as d,
  f as e,
  y as f,
  h as g,
  I as h,
  T as m,
  A as r,
  g as s
};
//# sourceMappingURL=api-client-C-llcIxj.js.map
