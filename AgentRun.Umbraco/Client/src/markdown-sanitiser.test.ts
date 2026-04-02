import { expect } from "@open-wc/testing";
import { sanitiseMarkdown } from "./utils/markdown-sanitiser.js";

describe("sanitiseMarkdown", () => {
  describe("headings", () => {
    it("converts # to h1", () => {
      expect(sanitiseMarkdown("# Hello")).to.contain("<h1>Hello</h1>");
    });

    it("converts ## to h2", () => {
      expect(sanitiseMarkdown("## World")).to.contain("<h2>World</h2>");
    });

    it("converts ###### to h6", () => {
      expect(sanitiseMarkdown("###### Deep")).to.contain("<h6>Deep</h6>");
    });
  });

  describe("inline formatting", () => {
    it("converts **text** to strong", () => {
      const result = sanitiseMarkdown("**bold**");
      expect(result).to.contain("<strong>bold</strong>");
    });

    it("converts *text* to em", () => {
      const result = sanitiseMarkdown("*italic*");
      expect(result).to.contain("<em>italic</em>");
    });

    it("converts `code` to code", () => {
      const result = sanitiseMarkdown("`inline code`");
      expect(result).to.contain("<code>inline code</code>");
    });
  });

  describe("code blocks", () => {
    it("converts fenced code blocks to pre>code", () => {
      const result = sanitiseMarkdown("```js\nconst x = 1;\n```");
      expect(result).to.contain("<pre><code>");
      expect(result).to.contain("const x = 1;");
    });
  });

  describe("lists", () => {
    it("converts - items to ul>li", () => {
      const result = sanitiseMarkdown("- one\n- two");
      expect(result).to.contain("<ul>");
      expect(result).to.contain("<li>");
    });

    it("converts 1. items to ol>li", () => {
      const result = sanitiseMarkdown("1. first\n2. second");
      expect(result).to.contain("<ol>");
      expect(result).to.contain("<li>");
    });
  });

  describe("links", () => {
    it("converts [text](url) to a with correct attributes", () => {
      const result = sanitiseMarkdown("[Click](https://example.com)");
      expect(result).to.contain("<a");
      expect(result).to.contain('href="https://example.com"');
      expect(result).to.contain('target="_blank"');
      expect(result).to.contain('rel="noopener"');
    });

    it("strips javascript: URLs", () => {
      const result = sanitiseMarkdown("[click](javascript:alert(1))");
      expect(result).to.not.contain("javascript:");
      expect(result).to.not.contain("<a");
    });
  });

  describe("blockquotes", () => {
    it("converts > text to blockquote", () => {
      const result = sanitiseMarkdown("> quote text");
      expect(result).to.contain("<blockquote>");
    });
  });

  describe("horizontal rules", () => {
    it("converts --- to hr", () => {
      const result = sanitiseMarkdown("---");
      expect(result).to.contain("<hr>");
    });
  });

  describe("paragraphs", () => {
    it("wraps plain text in p", () => {
      const result = sanitiseMarkdown("Just some text");
      expect(result).to.contain("<p>");
      expect(result).to.contain("Just some text");
    });
  });

  describe("XSS sanitisation", () => {
    it("removes script tags", () => {
      const result = sanitiseMarkdown("<script>alert(1)</script>");
      expect(result).to.not.contain("<script>");
      // Text content may survive as inert text — that's safe. The key is no script element.
    });

    it("removes onclick attributes", () => {
      const result = sanitiseMarkdown('<p onclick="alert(1)">text</p>');
      expect(result).to.not.contain("onclick");
    });

    it("removes img with javascript src", () => {
      const result = sanitiseMarkdown('<img src="javascript:alert(1)">');
      expect(result).to.not.contain("<img");
      expect(result).to.not.contain("javascript:");
    });

    it("removes event handler attributes from allowed elements", () => {
      const result = sanitiseMarkdown('<a href="https://ok.com" onmouseover="alert(1)">link</a>');
      expect(result).to.not.contain("onmouseover");
      expect(result).to.contain("https://ok.com");
    });

    it("strips data: URLs in links", () => {
      const result = sanitiseMarkdown("[click](data:text/html,<script>alert(1)</script>)");
      expect(result).to.not.contain("data:");
      expect(result).to.not.contain("<a");
    });

    it("escapes HTML inside inline code", () => {
      const result = sanitiseMarkdown("`<img src=x onerror=alert(1)>`");
      expect(result).to.contain("<code>");
      expect(result).to.not.contain("<img");
      expect(result).to.contain("&lt;img");
    });

    it("escapes HTML in link text", () => {
      const result = sanitiseMarkdown("[<img src=x onerror=alert(1)>](https://example.com)");
      expect(result).to.not.contain("<img");
      expect(result).to.contain("&lt;img");
    });
  });
});
