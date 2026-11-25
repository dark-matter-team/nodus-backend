using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Nodus.Agent.Pipeline;

/// <summary>
/// Research cache per goal. Research is the most expensive part (searches + lots of tokens),
/// and for popular goals it is the same for everyone. Personalization happens at the build step.
/// </summary>
public interface IResearchCache
{
    bool TryGet(string key, out ResearchResult result);
    void Set(string key, ResearchResult result);
}

/// <summary>
/// In process memory: lost on restart. For production, replace with a table in Postgres or Redis.
/// </summary>
public sealed class InMemoryResearchCache(IOptions<AgentOptions> options, TimeProvider time) : IResearchCache
{
    private readonly ConcurrentDictionary<string, ResearchResult> _items = new();

    public bool TryGet(string key, out ResearchResult result)
    {
        var ttl = TimeSpan.FromHours(options.Value.ResearchCacheHours);
        if (_items.TryGetValue(key, out var cached) && time.GetUtcNow() - cached.CreatedAt < ttl)
        {
            result = cached;
            return true;
        }

        result = null!;
        return false;
    }

    public void Set(string key, ResearchResult result) => _items[key] = result;
}
