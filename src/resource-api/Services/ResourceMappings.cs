namespace ResourceApi.Services;

public static class ResourceMappings
{
    public static object ToPackSummary(this PackDefinition pack, HttpContext httpContext) => new
    {
        id = pack.Id,
        name = pack.Name,
        description = pack.Description,
        visibility = pack.Visibility,
        status = pack.Status,
        order = pack.SortOrder,
        puzzleCount = pack.PuzzleIds.Count,
        updatedAtUtc = pack.UpdatedAtUtc
    };

    public static object ToCmsResponse(this PackDefinition pack, ResourceStore store, HttpContext httpContext) => new
    {
        id = pack.Id,
        name = pack.Name,
        description = pack.Description,
        visibility = pack.Visibility,
        status = pack.Status,
        sortOrder = pack.SortOrder,
        puzzleIds = pack.PuzzleIds,
        puzzles = pack.PuzzleIds
            .Select(id => store.GetPuzzleById(id))
            .Where(puzzle => puzzle is not null)
            .Select(puzzle => new
            {
                id = puzzle!.Id,
                answer = puzzle.Answer,
                difficulty = puzzle.Difficulty
            }),
        updatedAtUtc = pack.UpdatedAtUtc
    };

    public static object ToResponse(this ImageAsset image, HttpContext httpContext) => new
    {
        id = image.Id,
        title = image.Title,
        url = BuildImageUrl(httpContext, image.Url),
        sourceType = image.SourceType,
        tags = image.Tags,
        createdAtUtc = image.CreatedAtUtc
    };

    public static object ToResponse(this PuzzleDefinition puzzle, ResourceStore store, HttpContext httpContext) => new
    {
        id = puzzle.Id,
        answer = puzzle.Answer,
        hint = puzzle.Hint,
        difficulty = puzzle.Difficulty,
        acceptableVariants = puzzle.AcceptableVariants,
        imageIds = puzzle.ImageIds,
        images = puzzle.ImageIds
            .Select(id => store.GetImageById(id))
            .Where(image => image is not null)
            .Select(image => image!.ToResponse(httpContext)),
        packIds = store.GetPackIdsForPuzzle(puzzle.Id),
        createdAtUtc = puzzle.CreatedAtUtc
    };

    public static object ToResponse(this NextPuzzleResult result, ResourceStore store, HttpContext httpContext)
    {
        if (result.PackCompleted || result.Puzzle is null)
        {
            return new
            {
                packId = result.PackId,
                packCompleted = true,
                puzzle = (object?)null
            };
        }

        return new
        {
            packId = result.PackId,
            packCompleted = false,
            puzzle = new
            {
                id = result.Puzzle.Id,
                answerLength = result.Puzzle.Answer.Length,
                hint = result.Puzzle.Hint,
                difficulty = result.Puzzle.Difficulty,
                images = result.Puzzle.ImageIds
                    .Select(id => store.GetImageById(id))
                    .Where(image => image is not null)
                    .Select(image => image!.ToResponse(httpContext))
            }
        };
    }

    private static string BuildImageUrl(HttpContext httpContext, string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.StartsWith("http", StringComparison.OrdinalIgnoreCase) || url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return url;
        }

        return $"{httpContext.Request.Scheme}://{httpContext.Request.Host}{url}";
    }
}
