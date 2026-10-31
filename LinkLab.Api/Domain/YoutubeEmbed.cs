using System.Net;
using System.Text.RegularExpressions;

namespace LinkLab.Api.Domain;

public static class YoutubeEmbed
{
    public static bool TryParse(string? input, out string videoId, out double aspectRatio)
    {
        videoId = string.Empty;
        aspectRatio = 16d / 9;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 10000) return false;
        var source = input.Trim();
        if (source.StartsWith("<", StringComparison.Ordinal))
        {
            var frame = Regex.Match(source, @"<iframe\b[^>]*>", RegexOptions.IgnoreCase);
            if (!frame.Success) return false;
            var src = Attribute(frame.Value, "src");
            if (src is null) return false;
            source = WebUtility.HtmlDecode(src);
            if (double.TryParse(Attribute(frame.Value, "width"), out var width) &&
                double.TryParse(Attribute(frame.Value, "height"), out var height) &&
                width > 0 && height > 0 && width / height >= 0.3 && width / height <= 3)
                aspectRatio = width / height;
        }
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        var host = uri.Host.ToLowerInvariant();
        if (host is not ("youtube.com" or "www.youtube.com" or "youtube-nocookie.com" or "www.youtube-nocookie.com")) return false;
        var path = Regex.Match(uri.AbsolutePath, @"^/embed/([A-Za-z0-9_-]{11})/?$");
        if (!path.Success) return false;
        videoId = path.Groups[1].Value;
        return true;
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, "\\s" + name + "\\s*=\\s*([\"'])(.*?)\\1", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[2].Value : null;
    }
}
