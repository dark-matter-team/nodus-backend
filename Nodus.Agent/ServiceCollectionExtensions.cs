using System.Net.Http.Headers;
using Anthropic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nodus.Agent.Claude;
using Nodus.Agent.Fake;
using Nodus.Agent.OpenAICompatible;
using Nodus.Agent.Pipeline;
using Nodus.Agent.Search;

namespace Nodus.Agent;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the agent. The backend only needs to call this and resolve <see cref="RoadmapGenerator"/> from DI.
    /// Keys are read from configuration first, then from the usual environment variables.
    /// </summary>
    public static IServiceCollection AddRoadmapAgent(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AgentOptions.SectionName);
        var options = section.Get<AgentOptions>() ?? new AgentOptions();
        var provider = Providers.Normalize(options.Provider);

        options.OpenAICompatible.ApiKey = FirstNonEmpty(options.OpenAICompatible.ApiKey, Env("GEMINI_API_KEY"));
        options.Tavily.ApiKey = FirstNonEmpty(options.Tavily.ApiKey, Env("TAVILY_API_KEY"));

        services.Configure<AgentOptions>(section);
        services.PostConfigure<AgentOptions>(o =>
        {
            o.OpenAICompatible.ApiKey = options.OpenAICompatible.ApiKey;
            o.Tavily.ApiKey = options.Tavily.ApiKey;
        });

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IResearchCache, InMemoryResearchCache>();
        services.AddSingleton<RoadmapGenerator>();

        switch (provider)
        {
            case Providers.Fake:
                services.AddSingleton<IResearcher, FakeResearcher>();
                services.AddSingleton<IRoadmapBuilder, FakeRoadmapBuilder>();
                services.AddSingleton<ILinkChecker, NoopLinkChecker>();
                break;

            case Providers.Claude:
                // Without an explicit key the SDK reads the ANTHROPIC_API_KEY environment variable.
                services.AddSingleton(_ => string.IsNullOrWhiteSpace(options.Claude.ApiKey)
                    ? new AnthropicClient()
                    : new AnthropicClient { ApiKey = options.Claude.ApiKey });
                services.AddSingleton<IResearcher, ClaudeResearcher>();
                services.AddSingleton<IRoadmapBuilder, ClaudeRoadmapBuilder>();
                AddLinkChecker(services);
                break;

            case Providers.OpenAICompatible:
                AddOpenAICompatible(services, options);
                AddLinkChecker(services);
                break;
        }

        return services;
    }

    private static void AddOpenAICompatible(IServiceCollection services, AgentOptions options)
    {
        var llm = options.OpenAICompatible;
        services.AddHttpClient(OpenAICompatibleChatClient.HttpClientName, http =>
        {
            http.BaseAddress = new Uri(llm.BaseUrl.EndsWith('/') ? llm.BaseUrl : llm.BaseUrl + "/");
            http.Timeout = TimeSpan.FromSeconds(llm.TimeoutSeconds);
            if (!string.IsNullOrWhiteSpace(llm.ApiKey))
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", llm.ApiKey);
            }
        });
        services.AddSingleton<OpenAICompatibleChatClient>();
        services.AddSingleton<IRoadmapBuilder, OpenAICompatibleRoadmapBuilder>();

        // Free web search if there is a Tavily key, otherwise research from the model's own knowledge.
        if (string.IsNullOrWhiteSpace(options.Tavily.ApiKey))
        {
            services.AddSingleton<IResearcher, NoSearchResearcher>();
            return;
        }

        services.AddHttpClient(TavilyResearcher.HttpClientName, http =>
        {
            http.BaseAddress = new Uri("https://api.tavily.com/");
            http.Timeout = TimeSpan.FromSeconds(30);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Tavily.ApiKey);
        });
        services.AddSingleton<IResearcher, TavilyResearcher>();
    }

    private static void AddLinkChecker(IServiceCollection services) =>
        services.AddHttpClient<ILinkChecker, HttpLinkChecker>(http =>
            {
                http.Timeout = TimeSpan.FromSeconds(8);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; RoadmapLinkChecker/0.1)");
            })
            // Redirects are not followed: a 3xx counts as a live link, and we never get bounced into an internal network.
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

    private static string? Env(string name) => Environment.GetEnvironmentVariable(name);

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
