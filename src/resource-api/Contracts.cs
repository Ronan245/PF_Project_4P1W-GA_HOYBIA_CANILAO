namespace ResourceApi;

public sealed record SubmitGuessRequest(string PackId, string PuzzleId, string Guess)
{
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(PackId) || string.IsNullOrWhiteSpace(PuzzleId))
        {
            return "PackId and PuzzleId are required.";
        }

        if (string.IsNullOrWhiteSpace(Guess))
        {
            return "Guess is required.";
        }

        return null;
    }
}

public sealed record RestartPackRequest(string PackId);

public sealed record CreateImageRequest(string Title, string Url);

public sealed record UpdateImageRequest(string Title, string Url);

public sealed record TagRequest(string Tag);

public sealed record TagListRequest(List<string> Tags);

public sealed record PuzzleUpsertRequest(
    string Answer,
    string? Hint,
    string Difficulty,
    List<string> ImageIds,
    List<string>? AcceptableVariants,
    List<string>? PackIds);

public sealed record PackUpsertRequest(
    string Name,
    string? Description,
    string Visibility,
    int SortOrder,
    List<string>? PuzzleIds);

public sealed record PublishPackRequest(bool? Published);

