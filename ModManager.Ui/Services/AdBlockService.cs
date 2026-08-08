using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.RegularExpressions;
using DistillNET;
using Microsoft.Extensions.Caching.Memory;

namespace ModManager.Ui.Services;

public sealed class AdBlockService : IDisposable
{
    private const string SubscriptionUrl = "https://easylist.to/easylist/easylist.txt";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly FilterDbCollection _filters = new(new MemoryCacheOptions());
    private string[] _blockedHosts = [];
    private readonly string _cachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ModManager",
        "easylist.txt");

    public AdBlockService()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string rules;
            try
            {
                rules = await _httpClient.GetStringAsync(SubscriptionUrl, cancellationToken);
                Directory.CreateDirectory(Path.GetDirectoryName(_cachePath)!);
                await File.WriteAllTextAsync(_cachePath, rules, cancellationToken);
            }
            catch (HttpRequestException) when (File.Exists(_cachePath))
            {
                rules = await File.ReadAllTextAsync(_cachePath, cancellationToken);
            }

            _filters.ParseStoreRules(rules.Split('\n'), 1);
            _filters.FinalizeForRead();
            _blockedHosts = ExtractBlockedHosts(rules);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Ad blocker refresh failed: {ex}");
        }
    }

    public bool IsBlocked(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        NameValueCollection options = new();
        return _filters.GetFiltersForDomain(uri.Host)
            .OfType<UrlFilter>()
            .Any(filter => filter.IsMatch(uri, options));
    }

    public IReadOnlyList<string> GetBlockedHosts() => _blockedHosts;

    public void Dispose()
    {
        _filters.Dispose();
        _httpClient.Dispose();
    }

    private static string[] ExtractBlockedHosts(string rules)
    {
        HashSet<string> hosts = new(StringComparer.OrdinalIgnoreCase);

        foreach (string rawLine in rules.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 ||
                line[0] == '!' ||
                line[0] == '[' ||
                line.StartsWith("@@", StringComparison.Ordinal) ||
                line.Contains("##", StringComparison.Ordinal) ||
                line.Contains("#@#", StringComparison.Ordinal))
            {
                continue;
            }

            string? host = TryExtractHost(line);
            if (host is not null)
            {
                hosts.Add(host);
            }
        }

        return [.. hosts];
    }

    private static string? TryExtractHost(string rule)
    {
        if (rule.StartsWith("||", StringComparison.Ordinal))
        {
            int end = rule.IndexOfAny(['^', '/', '$', '*']);
            string host = (end >= 0 ? rule[2..end] : rule[2..]).Trim('.');
            return IsValidHost(host) ? host : null;
        }

        if (rule.StartsWith("|http://", StringComparison.Ordinal) ||
            rule.StartsWith("|https://", StringComparison.Ordinal))
        {
            string value = rule[1..];
            int optionIndex = value.IndexOf('$');
            string url = optionIndex >= 0 ? value[..optionIndex] : value;
            return Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && IsValidHost(uri.Host)
                ? uri.Host
                : null;
        }

        Match match = Regex.Match(rule, @"^https?://([^/$^*]+)");
        return match.Success && IsValidHost(match.Groups[1].Value)
            ? match.Groups[1].Value
            : null;
    }

    private static bool IsValidHost(string host) =>
        host.Length > 0 &&
        host.Contains('.', StringComparison.Ordinal) &&
        host.All(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-');
}
