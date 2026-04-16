const ALLOWED_TAGS = new Set([
  "p", "h1", "h2", "h3", "h4", "h5", "h6",
  "ul", "ol", "li", "a", "strong", "em", "code", "pre",
  "blockquote", "hr", "br", "table", "tr", "td", "th", "thead", "tbody",
]);

/**
 * Converts a basic markdown subset to sanitised HTML.
 * No external dependencies — handles the common subset LLMs produce.
 */
export function sanitiseMarkdown(raw: string): string {
  const html = markdownToHtml(raw);
  return sanitiseHtml(html);
}

// ---------- Markdown → HTML ----------

function markdownToHtml(raw: string): string {
  // 1. Extract fenced code blocks
  const codeBlocks: string[] = [];
  let text = raw.replace(/```[\s\S]*?```/g, (match) => {
    const idx = codeBlocks.length;
    const inner = match.replace(/^```[^\n]*\n?/, "").replace(/\n?```$/, "");
    codeBlocks.push(`<pre><code>${escapeHtml(inner)}</code></pre>`);
    return `\x00CB${idx}\x00`;
  });

  // 2. Process lines
  const lines = text.split("\n");
  const output: string[] = [];
  let i = 0;

  while (i < lines.length) {
    const line = lines[i];

    // Code block placeholder
    const cbMatch = line.match(/^\x00CB(\d+)\x00$/);
    if (cbMatch) {
      output.push(codeBlocks[parseInt(cbMatch[1], 10)]);
      i++;
      continue;
    }

    // Horizontal rule
    if (/^(---+|\*\*\*+|___+)\s*$/.test(line)) {
      output.push("<hr>");
      i++;
      continue;
    }

    // Heading
    const headingMatch = line.match(/^(#{1,6})\s+(.+)$/);
    if (headingMatch) {
      const level = headingMatch[1].length;
      output.push(`<h${level}>${processInline(headingMatch[2])}</h${level}>`);
      i++;
      continue;
    }

    // Blockquote
    if (line.startsWith("> ")) {
      const bqLines: string[] = [];
      while (i < lines.length && lines[i].startsWith("> ")) {
        bqLines.push(lines[i].slice(2));
        i++;
      }
      output.push(`<blockquote>${processInline(bqLines.join("<br>"))}</blockquote>`);
      continue;
    }

    // Unordered list
    if (/^[-*]\s+/.test(line)) {
      const items: string[] = [];
      while (i < lines.length && /^[-*]\s+/.test(lines[i])) {
        items.push(lines[i].replace(/^[-*]\s+/, ""));
        i++;
      }
      output.push("<ul>" + items.map(it => `<li>${processInline(it)}</li>`).join("") + "</ul>");
      continue;
    }

    // Ordered list
    if (/^\d+\.\s+/.test(line)) {
      const items: string[] = [];
      while (i < lines.length && /^\d+\.\s+/.test(lines[i])) {
        items.push(lines[i].replace(/^\d+\.\s+/, ""));
        i++;
      }
      output.push("<ol>" + items.map(it => `<li>${processInline(it)}</li>`).join("") + "</ol>");
      continue;
    }

    // Empty line
    if (line.trim() === "") {
      i++;
      continue;
    }

    // Paragraph — collect consecutive non-blank, non-special lines
    const paraLines: string[] = [];
    while (
      i < lines.length &&
      lines[i].trim() !== "" &&
      !/^(#{1,6}\s|[-*]\s|>\s|\d+\.\s|---+|___+|\*\*\*+|\x00CB)/.test(lines[i])
    ) {
      paraLines.push(lines[i]);
      i++;
    }
    if (paraLines.length > 0) {
      output.push(`<p>${processInline(paraLines.join("<br>"))}</p>`);
    }
  }

  return output.join("");
}

function processInline(text: string): string {
  // Inline code (must come before bold/italic to avoid conflicts)
  text = text.replace(/`([^`]+)`/g, (_m, code: string) => `<code>${escapeHtml(code)}</code>`);
  // Bold
  text = text.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");
  text = text.replace(/__(.+?)__/g, "<strong>$1</strong>");
  // Italic
  text = text.replace(/\*(.+?)\*/g, "<em>$1</em>");
  text = text.replace(/_(.+?)_/g, "<em>$1</em>");
  // Links
  text = text.replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_match, linkText: string, url: string) => {
    if (/^https?:\/\//i.test(url)) {
      return `<a href="${escapeAttr(url)}" target="_blank" rel="noopener">${escapeHtml(linkText)}</a>`;
    }
    return linkText; // Strip non-http links
  });
  return text;
}

function escapeHtml(str: string): string {
  return str
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function escapeAttr(str: string): string {
  return str
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
}

// ---------- HTML sanitisation (whitelist) ----------

function sanitiseHtml(html: string): string {
  const template = document.createElement("template");
  template.innerHTML = html;
  sanitiseNode(template.content);
  const div = document.createElement("div");
  div.appendChild(template.content.cloneNode(true));
  return div.innerHTML;
}

function sanitiseNode(node: Node): void {
  const children = Array.from(node.childNodes);
  for (const child of children) {
    if (child.nodeType === Node.ELEMENT_NODE) {
      const el = child as Element;
      const tag = el.tagName.toLowerCase();
      if (!ALLOWED_TAGS.has(tag)) {
        // Replace with text content
        const text = document.createTextNode(el.textContent ?? "");
        node.replaceChild(text, child);
      } else {
        // Strip all attributes except allowed ones on <a>
        const attrs = Array.from(el.attributes);
        for (const attr of attrs) {
          if (tag === "a" && (attr.name === "href" || attr.name === "target" || attr.name === "rel")) {
            continue;
          }
          el.removeAttribute(attr.name);
        }
        // Validate <a> href
        if (tag === "a") {
          const href = el.getAttribute("href") ?? "";
          if (!/^https?:\/\//i.test(href)) {
            el.removeAttribute("href");
          }
          el.setAttribute("target", "_blank");
          el.setAttribute("rel", "noopener");
        }
        sanitiseNode(el);
      }
    } else if (child.nodeType !== Node.TEXT_NODE) {
      node.removeChild(child);
    }
  }
}
