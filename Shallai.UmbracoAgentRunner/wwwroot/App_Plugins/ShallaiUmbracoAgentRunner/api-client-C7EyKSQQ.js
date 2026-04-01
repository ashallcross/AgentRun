function f(e, t) {
  if (!t) return e;
  if (typeof t.path == "string") {
    const n = t.path.split("/");
    return n[n.length - 1] || e;
  }
  if (typeof t.url == "string")
    return t.url.length > 60 ? t.url.slice(0, 60) + "…" : t.url;
  for (const n of Object.values(t))
    if (typeof n == "string") return n;
  return e;
}
const a = "/umbraco/api/shallai";
async function l(e, t) {
  const n = { Accept: "application/json" };
  t && (n.Authorization = `Bearer ${t}`);
  const o = await fetch(`${a}${e}`, { headers: n });
  if (!o.ok)
    throw new Error(`API error: ${o.status} ${o.statusText}`);
  return o.json();
}
async function i(e, t, n) {
  const o = {
    Accept: "application/json",
    "Content-Type": "application/json"
  };
  n && (o.Authorization = `Bearer ${n}`);
  const s = await fetch(`${a}${e}`, {
    method: "POST",
    headers: o,
    body: JSON.stringify(t)
  });
  if (!s.ok)
    throw new Error(`API error: ${s.status} ${s.statusText}`);
  return s.json();
}
async function p(e, t) {
  const n = {};
  t && (n.Authorization = `Bearer ${t}`);
  const o = await fetch(`${a}${e}`, {
    method: "DELETE",
    headers: n
  });
  if (!o.ok)
    throw new Error(`API error: ${o.status} ${o.statusText}`);
}
function d(e) {
  return l("/workflows", e);
}
function I(e, t) {
  return l(`/instances?workflowAlias=${encodeURIComponent(e)}`, t);
}
function $(e, t) {
  return i("/instances", { workflowAlias: e }, t);
}
function C(e, t) {
  return p(`/instances/${encodeURIComponent(e)}`, t);
}
function g(e, t) {
  return l(`/instances/${encodeURIComponent(e)}`, t);
}
function y(e, t) {
  return i(`/instances/${encodeURIComponent(e)}/cancel`, {}, t);
}
async function A(e, t) {
  const n = {};
  return t && (n.Authorization = `Bearer ${t}`), fetch(`${a}/instances/${encodeURIComponent(e)}/start`, {
    method: "POST",
    headers: n
  });
}
function w(e, t, n) {
  return l(
    `/instances/${encodeURIComponent(e)}/conversation/${encodeURIComponent(t)}`,
    n
  );
}
function T(e) {
  const t = [];
  for (const n of e)
    if (n.role === "assistant" && n.content != null && !n.toolCallId)
      t.push({
        role: "agent",
        content: n.content,
        timestamp: n.timestamp
      });
    else if (n.role === "assistant" && n.toolCallId) {
      let o = null;
      if (n.toolArguments)
        try {
          o = JSON.parse(n.toolArguments);
        } catch {
          o = null;
        }
      const s = {
        toolCallId: n.toolCallId,
        toolName: n.toolName ?? "unknown",
        summary: f(n.toolName ?? "unknown", o),
        arguments: o,
        result: null,
        status: "complete"
      }, r = m(t);
      r ? r.toolCalls = [...r.toolCalls ?? [], s] : t.push({
        role: "agent",
        content: "",
        timestamp: n.timestamp,
        toolCalls: [s]
      });
    } else if (n.role === "tool" && n.toolCallId) {
      const o = n.toolResult ?? null, s = typeof o == "string" && o.startsWith("Tool '") && (o.includes("error") || o.includes("failed"));
      for (let r = t.length - 1; r >= 0; r--) {
        const c = t[r].toolCalls?.find((u) => u.toolCallId === n.toolCallId);
        if (c) {
          c.result = o, s && (c.status = "error");
          break;
        }
      }
    } else n.role === "system" && n.content != null && t.push({
      role: "system",
      content: n.content,
      timestamp: n.timestamp
    });
  return t;
}
function m(e) {
  for (let t = e.length - 1; t >= 0; t--)
    if (e[t].role === "agent") return e[t];
  return null;
}
export {
  I as a,
  g as b,
  $ as c,
  C as d,
  w as e,
  f,
  d as g,
  y as h,
  T as m,
  A as s
};
//# sourceMappingURL=api-client-C7EyKSQQ.js.map
