import { expect } from "@open-wc/testing";
import { sanitiseMarkdown } from "../utils/markdown-sanitiser.js";

describe("shallai-markdown-renderer", () => {
  describe("sanitiseMarkdown rendering integration", () => {
    it("converts markdown headings to HTML", () => {
      const result = sanitiseMarkdown("# Hello World");
      expect(result).to.include("<h1>");
      expect(result).to.include("Hello World");
    });

    it("converts bold and italic inline syntax", () => {
      const result = sanitiseMarkdown("**bold** and *italic*");
      expect(result).to.include("<strong>bold</strong>");
      expect(result).to.include("<em>italic</em>");
    });

    it("sanitises script tags from HTML", () => {
      const result = sanitiseMarkdown("<script>alert('xss')</script>");
      expect(result).to.not.include("<script>");
    });

    it("converts links with target blank and noopener", () => {
      const result = sanitiseMarkdown("[Click](https://example.com)");
      expect(result).to.include('target="_blank"');
      expect(result).to.include('rel="noopener"');
      expect(result).to.include('href="https://example.com"');
    });

    it("handles empty input", () => {
      const result = sanitiseMarkdown("");
      expect(result).to.equal("");
    });

    it("converts code blocks with pre and code tags", () => {
      const result = sanitiseMarkdown("```\nconst x = 1;\n```");
      expect(result).to.include("<pre>");
      expect(result).to.include("<code>");
    });

    it("renders multiple heading levels", () => {
      const result = sanitiseMarkdown("## Heading 2\n### Heading 3");
      expect(result).to.include("<h2>");
      expect(result).to.include("<h3>");
    });

    it("renders unordered lists", () => {
      const result = sanitiseMarkdown("- item 1\n- item 2");
      expect(result).to.include("<ul>");
      expect(result).to.include("<li>");
    });

    it("renders blockquotes", () => {
      const result = sanitiseMarkdown("> quoted text");
      expect(result).to.include("<blockquote>");
    });
  });
});
