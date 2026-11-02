using System.Text.Json.Nodes;
using HalalBook.Api.Endpoints;
using HalalBook.Shared;

namespace HalalBook.Api.Services;

public partial class AiService
{
    /// <summary>Looks at a photo of an item and drafts a listing: title, description, category, condition and a fair price range.</summary>
    public async Task<ListingDraftDto?> DraftListingAsync(byte[] photo, string mediaType, string currency, CancellationToken ct)
    {
        if (!IsEnabled || photo.Length > 5_000_000) return null;
        var reply = await CompleteWithImageAsync(
            "You help people sell second-hand items on a local marketplace. Look at the photo and draft a listing. " +
            "Reply with only JSON: {\"title\": short and specific (brand, item, size or colour if visible), " +
            "\"description\": 2-3 friendly sentences on what it is and what's visible about its condition (don't invent specs), " +
            "\"category\": one of " + string.Join(", ", MarketEndpoints.Categories) + ", \"condition\": one of New, Like new, Good, Fair, " +
            "\"fairLow\": number, \"fairHigh\": number (a fair used price range in " + currency + ")}.",
            photo, mediaType, "Draft a listing for this item.", 500, ct);
        try
        {
            var j = JsonNode.Parse(reply![reply.IndexOf('{')..(reply.LastIndexOf('}') + 1)])!;
            var category = j["category"]?.GetValue<string>();
            decimal? Num(string k) => j[k] is JsonValue v && v.TryGetValue<decimal>(out var d) && d >= 0 ? Math.Round(d) : null;
            return new ListingDraftDto(j["title"]?.GetValue<string>() ?? "", j["description"]?.GetValue<string>() ?? "",
                MarketEndpoints.Categories.Contains(category) ? category! : "Other", j["condition"]?.GetValue<string>(),
                Num("fairLow"), Num("fairHigh"), currency, true);
        }
        catch (Exception) { return null; }
    }
}
