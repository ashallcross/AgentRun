using System;

namespace AgentRun.Umbraco.Tools;

public interface IHtmlStructureExtractor
{
    StructuredHtmlContent Extract(byte[] body, Uri sourceUri, int unmarkedLength, bool truncated);
}
