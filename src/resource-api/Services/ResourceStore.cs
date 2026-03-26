using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ResourceApi.Services;

public sealed class ResourceStore
{
    private readonly object _sync = new();
    private readonly string _filePath;
    private readonly string _uploadDirectory;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly GameplayOptions _options;
    private readonly Random _random = new();
    private ResourceState _state;

    public ResourceStore(IHostEnvironment environment, IOptions<GameplayOptions> options)
    {
        _options = options.Value;

        var dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "resource-state.json");

        var webRootPath = Path.Combine(environment.ContentRootPath, "wwwroot");
        Directory.CreateDirectory(webRootPath);
        _uploadDirectory = Path.Combine(webRootPath, "uploads");
        Directory.CreateDirectory(_uploadDirectory);

        _state = LoadState();
        if (_state.Images.Count == 0 && _state.Puzzles.Count == 0 && _state.Packs.Count == 0)
        {
            Seed();
        }

        UpgradeSeedContent();
    }

    public IReadOnlyList<PackDefinition> GetPublishedPacks(bool random)
    {
        lock (_sync)
        {
            var packs = _state.Packs
                .Where(pack => string.Equals(pack.Status, "published", StringComparison.OrdinalIgnoreCase))
                .OrderBy(pack => pack.SortOrder)
                .ThenBy(pack => pack.Name)
                .ToList();

            return random ? packs.OrderBy(_ => _random.Next()).ToList() : packs;
        }
    }

    public NextPuzzleResult? GetNextPuzzle(string packId, string userId)
    {
        lock (_sync)
        {
            var pack = _state.Packs.FirstOrDefault(candidate => candidate.Id == packId && candidate.Status == "published");
            if (pack is null)
            {
                return null;
            }

            var progress = GetOrCreateProgress(userId);
            var cooldownCutoff = DateTime.UtcNow.AddHours(-_options.SolvedCooldownHours);

            var neverSolved = pack.PuzzleIds
                .Select(GetPuzzleById)
                .Where(puzzle => puzzle is not null && !progress.SolvedAtByPuzzleId.ContainsKey(puzzle.Id))
                .Cast<PuzzleDefinition>()
                .ToList();

            var availableAfterCooldown = pack.PuzzleIds
                .Select(GetPuzzleById)
                .Where(puzzle =>
                    puzzle is not null &&
                    (!progress.SolvedAtByPuzzleId.TryGetValue(puzzle.Id, out var solvedAt) || solvedAt < cooldownCutoff))
                .Cast<PuzzleDefinition>()
                .ToList();

            var pool = neverSolved.Count > 0 ? neverSolved : availableAfterCooldown;
            if (pool.Count == 0)
            {
                return new NextPuzzleResult
                {
                    PackId = packId,
                    PackCompleted = true
                };
            }

            return new NextPuzzleResult
            {
                PackId = packId,
                Puzzle = pool[_random.Next(pool.Count)]
            };
        }
    }

    public object? SubmitGuess(string userId, SubmitGuessRequest request)
    {
        lock (_sync)
        {
            var puzzle = GetPuzzleById(request.PuzzleId);
            if (puzzle is null)
            {
                return null;
            }

            var progress = GetOrCreateProgress(userId);
            progress.AttemptsByPuzzleId.TryGetValue(puzzle.Id, out var attemptCount);
            progress.AttemptsByPuzzleId[puzzle.Id] = attemptCount + 1;

            var acceptableAnswers = new HashSet<string>(
                [Normalize(puzzle.Answer), .. puzzle.AcceptableVariants.Select(Normalize)],
                StringComparer.OrdinalIgnoreCase);

            var correct = acceptableAnswers.Contains(Normalize(request.Guess));
            var scoreDelta = 0;

            if (correct)
            {
                if (!progress.SolvedAtByPuzzleId.ContainsKey(puzzle.Id))
                {
                    progress.SolvedAtByPuzzleId[puzzle.Id] = DateTime.UtcNow;
                    scoreDelta = _options.CorrectScore;
                    progress.Score += scoreDelta;
                }
            }
            else
            {
                scoreDelta = -Math.Abs(_options.IncorrectPenalty);
                progress.Score += scoreDelta;
            }

            SaveState();

            var nextPuzzle = GetNextPuzzle(request.PackId, userId);
            return new
            {
                correct,
                scoreDelta,
                nextAvailable = nextPuzzle is { PackCompleted: false, Puzzle: not null },
                score = progress.Score
            };
        }
    }

    public void RestartPack(string userId, string packId)
    {
        lock (_sync)
        {
            var pack = GetPackById(packId);
            if (pack is null)
            {
                return;
            }

            var progress = GetOrCreateProgress(userId);
            foreach (var puzzleId in pack.PuzzleIds)
            {
                progress.SolvedAtByPuzzleId.Remove(puzzleId);
                progress.AttemptsByPuzzleId.Remove(puzzleId);
            }

            SaveState();
        }
    }

    public object GetProfileProgress(string userId)
    {
        lock (_sync)
        {
            var progress = GetOrCreateProgress(userId);
            var recent = progress.SolvedAtByPuzzleId
                .OrderByDescending(entry => entry.Value)
                .Take(8)
                .Select(entry =>
                {
                    var puzzle = GetPuzzleById(entry.Key);
                    return new
                    {
                        puzzleId = entry.Key,
                        answer = puzzle?.Answer ?? "Unknown",
                        solvedAtUtc = entry.Value,
                        attempts = progress.AttemptsByPuzzleId.GetValueOrDefault(entry.Key)
                    };
                })
                .ToList();

            return new
            {
                solved = progress.SolvedAtByPuzzleId.Count,
                attempts = progress.AttemptsByPuzzleId.Values.Sum(),
                score = progress.Score,
                recentPuzzles = recent
            };
        }
    }

    public IReadOnlyList<ImageAsset> GetImages(string? tag)
    {
        lock (_sync)
        {
            var images = _state.Images.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(tag))
            {
                images = images.Where(image => image.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase));
            }

            return images.OrderByDescending(image => image.CreatedAtUtc).ToList();
        }
    }

    public ImageAsset AddUrlImage(CreateImageRequest request)
    {
        lock (_sync)
        {
            var image = new ImageAsset
            {
                Title = string.IsNullOrWhiteSpace(request.Title) ? "URL image" : request.Title.Trim(),
                Url = request.Url.Trim(),
                SourceType = "url",
                CreatedAtUtc = DateTime.UtcNow
            };

            _state.Images.Add(image);
            SaveState();
            return image;
        }
    }

    public async Task<IReadOnlyList<ImageAsset>> AddUploadedImagesAsync(IFormFileCollection files, string title)
    {
        var created = new List<ImageAsset>();

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".webp"))
            {
                continue;
            }

            var fileName = $"{Guid.NewGuid():N}{extension}";
            var path = Path.Combine(_uploadDirectory, fileName);
            await using var stream = File.Create(path);
            await file.CopyToAsync(stream);

            created.Add(new ImageAsset
            {
                Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(file.FileName) : title.Trim(),
                Url = $"/uploads/{fileName}",
                SourceType = "file",
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        lock (_sync)
        {
            _state.Images.AddRange(created);
            SaveState();
        }

        return created;
    }

    public ImageAsset? UpdateImage(string id, UpdateImageRequest request)
    {
        lock (_sync)
        {
            var image = GetImageById(id);
            if (image is null)
            {
                return null;
            }

            image.Title = string.IsNullOrWhiteSpace(request.Title) ? image.Title : request.Title.Trim();
            if (!string.IsNullOrWhiteSpace(request.Url))
            {
                image.Url = request.Url.Trim();
                image.SourceType = image.Url.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase) ? "file" : "url";
            }

            SaveState();
            return image;
        }
    }

    public bool DeleteImage(string id)
    {
        lock (_sync)
        {
            if (_state.Puzzles.Any(puzzle => puzzle.ImageIds.Contains(id)))
            {
                return false;
            }

            var image = GetImageById(id);
            if (image is null)
            {
                return false;
            }

            _state.Images.Remove(image);
            SaveState();
            return true;
        }
    }

    public IReadOnlyList<string> GetTags()
    {
        lock (_sync)
        {
            return _state.Tags.OrderBy(tag => tag).ToList();
        }
    }

    public string? AddTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        lock (_sync)
        {
            var normalized = tag.Trim().ToLowerInvariant();
            if (!_state.Tags.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                _state.Tags.Add(normalized);
                SaveState();
            }

            return normalized;
        }
    }

    public bool DeleteTag(string tag)
    {
        lock (_sync)
        {
            if (_state.Images.Any(image => image.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase)))
            {
                return false;
            }

            var removed = _state.Tags.RemoveAll(existing => existing.Equals(tag, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                SaveState();
            }

            return removed;
        }
    }

    public ImageAsset? AddTagsToImage(string id, IEnumerable<string> tags)
    {
        lock (_sync)
        {
            var image = GetImageById(id);
            if (image is null)
            {
                return null;
            }

            foreach (var tag in tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(tag => tag.Trim().ToLowerInvariant()).Distinct())
            {
                if (!_state.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    _state.Tags.Add(tag);
                }

                if (!image.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                {
                    image.Tags.Add(tag);
                }
            }

            image.Tags = image.Tags.OrderBy(tag => tag).ToList();
            SaveState();
            return image;
        }
    }

    public ImageAsset? RemoveTagFromImage(string id, string tag)
    {
        lock (_sync)
        {
            var image = GetImageById(id);
            if (image is null)
            {
                return null;
            }

            image.Tags.RemoveAll(existing => existing.Equals(tag, StringComparison.OrdinalIgnoreCase));
            SaveState();
            return image;
        }
    }

    public IReadOnlyList<PuzzleDefinition> GetPuzzles()
    {
        lock (_sync)
        {
            return _state.Puzzles.OrderBy(puzzle => puzzle.Answer).ToList();
        }
    }

    public MutationResult<PuzzleDefinition> CreatePuzzle(PuzzleUpsertRequest request)
    {
        lock (_sync)
        {
            var error = ValidatePuzzleRequest(null, request);
            if (error is not null)
            {
                return new MutationResult<PuzzleDefinition> { Error = error };
            }

            var puzzle = new PuzzleDefinition
            {
                Answer = request.Answer.Trim(),
                Hint = string.IsNullOrWhiteSpace(request.Hint) ? null : request.Hint.Trim(),
                Difficulty = string.IsNullOrWhiteSpace(request.Difficulty) ? "medium" : request.Difficulty.Trim().ToLowerInvariant(),
                ImageIds = request.ImageIds.Distinct().ToList(),
                AcceptableVariants = request.AcceptableVariants?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [],
                CreatedAtUtc = DateTime.UtcNow
            };

            _state.Puzzles.Add(puzzle);
            SyncPuzzlePacks(puzzle.Id, request.PackIds ?? []);
            SaveState();

            return new MutationResult<PuzzleDefinition> { Value = puzzle };
        }
    }

    public MutationResult<PuzzleDefinition> UpdatePuzzle(string id, PuzzleUpsertRequest request)
    {
        lock (_sync)
        {
            var puzzle = GetPuzzleById(id);
            if (puzzle is null)
            {
                return new MutationResult<PuzzleDefinition> { Error = "not-found" };
            }

            var error = ValidatePuzzleRequest(id, request);
            if (error is not null)
            {
                return new MutationResult<PuzzleDefinition> { Error = error };
            }

            puzzle.Answer = request.Answer.Trim();
            puzzle.Hint = string.IsNullOrWhiteSpace(request.Hint) ? null : request.Hint.Trim();
            puzzle.Difficulty = string.IsNullOrWhiteSpace(request.Difficulty) ? "medium" : request.Difficulty.Trim().ToLowerInvariant();
            puzzle.ImageIds = request.ImageIds.Distinct().ToList();
            puzzle.AcceptableVariants = request.AcceptableVariants?.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];

            SyncPuzzlePacks(id, request.PackIds ?? []);
            SaveState();
            return new MutationResult<PuzzleDefinition> { Value = puzzle };
        }
    }

    public bool DeletePuzzle(string id)
    {
        lock (_sync)
        {
            var puzzle = GetPuzzleById(id);
            if (puzzle is null)
            {
                return false;
            }

            _state.Puzzles.Remove(puzzle);
            foreach (var pack in _state.Packs)
            {
                pack.PuzzleIds.RemoveAll(puzzleId => puzzleId == id);
            }

            SaveState();
            return true;
        }
    }

    public IReadOnlyList<PackDefinition> GetAllPacks()
    {
        lock (_sync)
        {
            return _state.Packs.OrderBy(pack => pack.SortOrder).ThenBy(pack => pack.Name).ToList();
        }
    }

    public MutationResult<PackDefinition> CreatePack(PackUpsertRequest request)
    {
        lock (_sync)
        {
            var error = ValidatePackRequest(null, request);
            if (error is not null)
            {
                return new MutationResult<PackDefinition> { Error = error };
            }

            var pack = new PackDefinition
            {
                Name = request.Name.Trim(),
                Description = request.Description?.Trim() ?? string.Empty,
                Visibility = string.IsNullOrWhiteSpace(request.Visibility) ? "public" : request.Visibility.Trim().ToLowerInvariant(),
                SortOrder = request.SortOrder,
                PuzzleIds = request.PuzzleIds?.Distinct().ToList() ?? [],
                UpdatedAtUtc = DateTime.UtcNow
            };

            _state.Packs.Add(pack);
            SaveState();
            return new MutationResult<PackDefinition> { Value = pack };
        }
    }

    public MutationResult<PackDefinition> UpdatePack(string id, PackUpsertRequest request)
    {
        lock (_sync)
        {
            var pack = GetPackById(id);
            if (pack is null)
            {
                return new MutationResult<PackDefinition> { Error = "not-found" };
            }

            var error = ValidatePackRequest(id, request);
            if (error is not null)
            {
                return new MutationResult<PackDefinition> { Error = error };
            }

            pack.Name = request.Name.Trim();
            pack.Description = request.Description?.Trim() ?? string.Empty;
            pack.Visibility = string.IsNullOrWhiteSpace(request.Visibility) ? "public" : request.Visibility.Trim().ToLowerInvariant();
            pack.SortOrder = request.SortOrder;
            pack.PuzzleIds = request.PuzzleIds?.Distinct().ToList() ?? [];
            pack.UpdatedAtUtc = DateTime.UtcNow;

            SaveState();
            return new MutationResult<PackDefinition> { Value = pack };
        }
    }

    public bool DeletePack(string id)
    {
        lock (_sync)
        {
            var pack = GetPackById(id);
            if (pack is null)
            {
                return false;
            }

            _state.Packs.Remove(pack);
            SaveState();
            return true;
        }
    }

    public PackDefinition? TogglePackPublish(string id, bool? published)
    {
        lock (_sync)
        {
            var pack = GetPackById(id);
            if (pack is null)
            {
                return null;
            }

            var shouldPublish = published ?? !string.Equals(pack.Status, "published", StringComparison.OrdinalIgnoreCase);
            pack.Status = shouldPublish ? "published" : "draft";
            pack.UpdatedAtUtc = DateTime.UtcNow;
            SaveState();
            return pack;
        }
    }

    public ImageAsset? GetImageById(string id) =>
        _state.Images.FirstOrDefault(image => image.Id == id);

    public PuzzleDefinition? GetPuzzleById(string id) =>
        _state.Puzzles.FirstOrDefault(puzzle => puzzle.Id == id);

    public PackDefinition? GetPackById(string id) =>
        _state.Packs.FirstOrDefault(pack => pack.Id == id);

    public IReadOnlyList<string> GetPackIdsForPuzzle(string puzzleId) =>
        _state.Packs.Where(pack => pack.PuzzleIds.Contains(puzzleId)).Select(pack => pack.Id).ToList();

    private string? ValidatePuzzleRequest(string? currentPuzzleId, PuzzleUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Answer))
        {
            return "Answer is required.";
        }

        var imageIds = request.ImageIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList() ?? [];
        if (imageIds.Count != 4)
        {
            return "A puzzle must contain exactly 4 unique images.";
        }

        if (imageIds.Any(id => GetImageById(id) is null))
        {
            return "One or more selected images do not exist.";
        }

        foreach (var packId in request.PackIds ?? [])
        {
            var pack = GetPackById(packId);
            if (pack is null)
            {
                return "One or more selected packs do not exist.";
            }

            var duplicate = pack.PuzzleIds
                .Where(id => id != currentPuzzleId)
                .Select(GetPuzzleById)
                .FirstOrDefault(puzzle => puzzle is not null && Normalize(puzzle.Answer) == Normalize(request.Answer));

            if (duplicate is not null)
            {
                return $"Answer '{request.Answer}' already exists in pack '{pack.Name}'.";
            }
        }

        return null;
    }

    private string? ValidatePackRequest(string? currentPackId, PackUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Pack name is required.";
        }

        var duplicateName = _state.Packs.Any(pack =>
            pack.Id != currentPackId &&
            string.Equals(pack.Name, request.Name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (duplicateName)
        {
            return "Pack name must be unique.";
        }

        var puzzleIds = request.PuzzleIds?.Distinct().ToList() ?? [];
        if (puzzleIds.Any(id => GetPuzzleById(id) is null))
        {
            return "One or more selected puzzles do not exist.";
        }

        var duplicateAnswers = puzzleIds
            .Select(GetPuzzleById)
            .Where(puzzle => puzzle is not null)
            .GroupBy(puzzle => Normalize(puzzle!.Answer))
            .Any(group => group.Count() > 1);

        return duplicateAnswers ? "Pack cannot contain duplicate answers." : null;
    }

    private void SyncPuzzlePacks(string puzzleId, IReadOnlyCollection<string> requestedPackIds)
    {
        foreach (var pack in _state.Packs)
        {
            var shouldContain = requestedPackIds.Contains(pack.Id);
            var contains = pack.PuzzleIds.Contains(puzzleId);

            if (shouldContain && !contains)
            {
                pack.PuzzleIds.Add(puzzleId);
                pack.UpdatedAtUtc = DateTime.UtcNow;
            }

            if (!shouldContain && contains)
            {
                pack.PuzzleIds.RemoveAll(id => id == puzzleId);
                pack.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
    }

    private PlayerProgress GetOrCreateProgress(string userId)
    {
        var progress = _state.ProgressEntries.FirstOrDefault(entry => entry.UserId == userId);
        if (progress is not null)
        {
            return progress;
        }

        progress = new PlayerProgress { UserId = userId };
        _state.ProgressEntries.Add(progress);
        SaveState();
        return progress;
    }

    private ResourceState LoadState()
    {
        if (!File.Exists(_filePath))
        {
            return new ResourceState();
        }

        var json = File.ReadAllText(_filePath);
        return JsonSerializer.Deserialize<ResourceState>(json, _jsonOptions) ?? new ResourceState();
    }

    private void SaveState()
    {
        File.WriteAllText(_filePath, JsonSerializer.Serialize(_state, _jsonOptions));
    }

    private static string Normalize(string value) =>
        new(value.Trim().ToUpperInvariant().Where(character => character is not (' ' or '-')).ToArray());

    private void Seed()
    {
        _state.Tags = ["food", "fruit", "weather", "school", "animal", "object"];

        var appleImages = CreateClueImages("APPLE", ["Red fruit", "Teacher gift", "Juice", "Pie"]);
        var rainImages = CreateClueImages("RAIN", ["Umbrella", "Cloud", "Puddle", "Wet street"]);
        var bookImages = CreateClueImages("BOOK", ["Library", "Pages", "Study", "Desk"]);

        appleImages.ForEach(image => image.Tags = ["food", "fruit"]);
        rainImages.ForEach(image => image.Tags = ["weather"]);
        bookImages.ForEach(image => image.Tags = ["school", "object"]);

        _state.Images.AddRange(appleImages);
        _state.Images.AddRange(rainImages);
        _state.Images.AddRange(bookImages);

        var applePuzzle = new PuzzleDefinition
        {
            Answer = "APPLE",
            Hint = "A common fruit.",
            Difficulty = "easy",
            ImageIds = appleImages.Select(image => image.Id).ToList(),
            AcceptableVariants = ["apple fruit"]
        };

        var rainPuzzle = new PuzzleDefinition
        {
            Answer = "RAIN",
            Hint = "Weather that gets you wet.",
            Difficulty = "easy",
            ImageIds = rainImages.Select(image => image.Id).ToList()
        };

        var bookPuzzle = new PuzzleDefinition
        {
            Answer = "BOOK",
            Hint = "Something you read.",
            Difficulty = "easy",
            ImageIds = bookImages.Select(image => image.Id).ToList()
        };

        _state.Puzzles.AddRange([applePuzzle, rainPuzzle, bookPuzzle]);

        _state.Packs.AddRange(
        [
            new PackDefinition
            {
                Name = "Starter Pack",
                Description = "Easy practice puzzles for first-time players.",
                Visibility = "public",
                Status = "published",
                SortOrder = 1,
                PuzzleIds = [applePuzzle.Id, rainPuzzle.Id]
            },
            new PackDefinition
            {
                Name = "School Days",
                Description = "Simple school-themed clues.",
                Visibility = "public",
                Status = "published",
                SortOrder = 2,
                PuzzleIds = [bookPuzzle.Id]
            }
        ]);

        SaveState();
    }

    private void UpgradeSeedContent()
    {
        var updated = false;

        foreach (var (answer, captions, tags) in GetSeedDefinitions())
        {
            var puzzle = _state.Puzzles.FirstOrDefault(item => string.Equals(item.Answer, answer, StringComparison.OrdinalIgnoreCase));
            if (puzzle is null || puzzle.ImageIds.Count != captions.Length)
            {
                continue;
            }

            for (var index = 0; index < captions.Length; index++)
            {
                var image = GetImageById(puzzle.ImageIds[index]);
                if (image is null || !string.Equals(image.SourceType, "seed", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var expectedTitle = captions[index];
                var expectedUrl = CreateHintSvgDataUrl(answer, captions[index], index);
                if (image.Title != expectedTitle || image.Url != expectedUrl)
                {
                    image.Title = expectedTitle;
                    image.Url = expectedUrl;
                    image.Tags = tags.ToList();
                    updated = true;
                }
            }
        }

        if (updated)
        {
            SaveState();
        }
    }

    private List<ImageAsset> CreateClueImages(string answer, IReadOnlyList<string> captions) =>
        captions.Select((caption, index) => new ImageAsset
        {
            Title = caption,
            Url = CreateHintSvgDataUrl(answer, caption, index),
            SourceType = "seed",
            CreatedAtUtc = DateTime.UtcNow
        }).ToList();

    private static string CreateHintSvgDataUrl(string answer, string caption, int variantIndex)
    {
        var scene = BuildHintScene(answer, caption);
        var palette = (variantIndex % 4) switch
        {
            0 => ("#f8edcf", "#f0a04b"),
            1 => ("#d8ecff", "#5e9ad6"),
            2 => ("#e8f4dc", "#74a95d"),
            _ => ("#f9dfe6", "#d46a8c")
        };

        var svg = $"""
        <svg xmlns="http://www.w3.org/2000/svg" width="640" height="480" viewBox="0 0 640 480">
          <defs>
            <linearGradient id="bg" x1="0%" y1="0%" x2="100%" y2="100%">
              <stop offset="0%" stop-color="{palette.Item1}"/>
              <stop offset="100%" stop-color="{palette.Item2}"/>
            </linearGradient>
          </defs>
          <rect width="640" height="480" rx="36" fill="url(#bg)"/>
          <rect x="28" y="28" width="584" height="424" rx="24" fill="rgba(255,255,255,0.72)"/>
          {scene}
        </svg>
        """;

        return $"data:image/svg+xml;charset=utf-8,{Uri.EscapeDataString(svg)}";
    }

    private static string BuildHintScene(string answer, string caption) =>
        (answer, caption) switch
        {
            ("APPLE", "Red fruit") => """
                <ellipse cx="320" cy="250" rx="112" ry="102" fill="#d83b3b"/>
                <rect x="311" y="126" width="18" height="44" rx="8" fill="#5d3b21"/>
                <path d="M326 138 C380 84, 432 110, 418 160 C384 170, 350 158, 326 138 Z" fill="#5d9746"/>
                """,
            ("APPLE", "Teacher gift") => """
                <rect x="205" y="188" width="230" height="170" rx="24" fill="#f3c14a"/>
                <rect x="304" y="166" width="32" height="194" rx="12" fill="#c74a4a"/>
                <rect x="182" y="246" width="276" height="26" rx="12" fill="#c74a4a"/>
                <circle cx="320" cy="162" r="40" fill="#c74a4a"/>
                """,
            ("APPLE", "Juice") => """
                <path d="M252 138 L388 138 L372 342 Q368 370 340 370 L300 370 Q272 370 268 342 Z" fill="#ffe8b1" stroke="#a86b2d" stroke-width="12"/>
                <rect x="270" y="206" width="100" height="126" rx="16" fill="#f2c24c"/>
                <rect x="382" y="118" width="14" height="120" rx="7" fill="#47a25f" transform="rotate(18 389 178)"/>
                """,
            ("APPLE", "Pie") => """
                <path d="M198 308 L430 308 Q404 170 316 170 Q232 170 198 308 Z" fill="#f1b563"/>
                <path d="M244 308 L394 308 Q382 222 318 222 Q260 222 244 308 Z" fill="#f6e5b6"/>
                <path d="M226 316 L414 316 L382 368 L258 368 Z" fill="#d58d49"/>
                """,
            ("RAIN", "Umbrella") => """
                <path d="M196 222 Q320 108 444 222 Z" fill="#5e7dd6"/>
                <rect x="312" y="222" width="16" height="122" rx="8" fill="#6a4526"/>
                <path d="M320 344 Q320 392 360 392" fill="none" stroke="#6a4526" stroke-width="14" stroke-linecap="round"/>
                <circle cx="190" cy="142" r="12" fill="#7ab5ff"/>
                <circle cx="238" cy="106" r="12" fill="#7ab5ff"/>
                <circle cx="436" cy="126" r="12" fill="#7ab5ff"/>
                """,
            ("RAIN", "Cloud") => """
                <circle cx="256" cy="212" r="64" fill="#c7d3e0"/>
                <circle cx="320" cy="188" r="84" fill="#d8e2ee"/>
                <circle cx="394" cy="218" r="58" fill="#c7d3e0"/>
                <rect x="236" y="216" width="188" height="74" rx="32" fill="#d8e2ee"/>
                <path d="M246 326 L226 374" stroke="#4a84d8" stroke-width="14" stroke-linecap="round"/>
                <path d="M320 326 L300 374" stroke="#4a84d8" stroke-width="14" stroke-linecap="round"/>
                <path d="M394 326 L374 374" stroke="#4a84d8" stroke-width="14" stroke-linecap="round"/>
                """,
            ("RAIN", "Puddle") => """
                <ellipse cx="320" cy="324" rx="172" ry="54" fill="#6aa9df"/>
                <ellipse cx="320" cy="324" rx="116" ry="34" fill="#8bc0ec"/>
                <path d="M260 164 L236 246" stroke="#6b7f8e" stroke-width="16" stroke-linecap="round"/>
                <path d="M380 150 L356 250" stroke="#6b7f8e" stroke-width="16" stroke-linecap="round"/>
                <circle cx="244" cy="134" r="12" fill="#7ab5ff"/>
                <circle cx="366" cy="122" r="12" fill="#7ab5ff"/>
                """,
            ("RAIN", "Wet street") => """
                <path d="M210 92 L168 392 L472 392 L430 92 Z" fill="#4f5d6f"/>
                <rect x="305" y="140" width="18" height="54" rx="8" fill="#f2e27b"/>
                <rect x="305" y="226" width="18" height="54" rx="8" fill="#f2e27b"/>
                <rect x="305" y="312" width="18" height="54" rx="8" fill="#f2e27b"/>
                <path d="M208 118 L196 168" stroke="#7ab5ff" stroke-width="12" stroke-linecap="round"/>
                <path d="M452 142 L440 192" stroke="#7ab5ff" stroke-width="12" stroke-linecap="round"/>
                """,
            ("BOOK", "Library") => """
                <rect x="166" y="126" width="308" height="214" rx="20" fill="#885530"/>
                <rect x="204" y="154" width="34" height="158" rx="8" fill="#d95b43"/>
                <rect x="250" y="154" width="34" height="158" rx="8" fill="#4f84c4"/>
                <rect x="296" y="154" width="34" height="158" rx="8" fill="#e3b74f"/>
                <rect x="342" y="154" width="34" height="158" rx="8" fill="#6a9b5f"/>
                <rect x="388" y="154" width="34" height="158" rx="8" fill="#9c5fa7"/>
                """,
            ("BOOK", "Pages") => """
                <path d="M188 144 Q276 124 320 188 L320 348 Q278 296 188 314 Z" fill="#fff8e8" stroke="#9d8b6f" stroke-width="10"/>
                <path d="M452 144 Q364 124 320 188 L320 348 Q362 296 452 314 Z" fill="#fff8e8" stroke="#9d8b6f" stroke-width="10"/>
                <line x1="240" y1="214" x2="292" y2="214" stroke="#cfbe9e" stroke-width="8"/>
                <line x1="350" y1="214" x2="402" y2="214" stroke="#cfbe9e" stroke-width="8"/>
                """,
            ("BOOK", "Study") => """
                <rect x="184" y="306" width="272" height="22" rx="10" fill="#96623c"/>
                <rect x="266" y="246" width="108" height="42" rx="14" fill="#4f84c4"/>
                <path d="M254 174 Q326 114 388 174" fill="none" stroke="#f0c95a" stroke-width="18" stroke-linecap="round"/>
                <circle cx="320" cy="170" r="42" fill="#f0c95a"/>
                """,
            ("BOOK", "Desk") => """
                <rect x="158" y="300" width="324" height="24" rx="12" fill="#8e5c38"/>
                <rect x="220" y="226" width="90" height="60" rx="12" fill="#d95b43"/>
                <rect x="290" y="198" width="102" height="80" rx="12" fill="#4f84c4"/>
                <rect x="372" y="234" width="70" height="44" rx="12" fill="#e3b74f"/>
                """,
            _ => """
                <circle cx="320" cy="240" r="110" fill="#6a9b5f"/>
                <circle cx="274" cy="216" r="24" fill="#ffffff"/>
                <circle cx="366" cy="216" r="24" fill="#ffffff"/>
                <path d="M264 300 Q320 344 376 300" fill="none" stroke="#ffffff" stroke-width="18" stroke-linecap="round"/>
                """
        };

    private static IEnumerable<(string Answer, string[] Captions, string[] Tags)> GetSeedDefinitions()
    {
        yield return ("APPLE", ["Red fruit", "Teacher gift", "Juice", "Pie"], ["food", "fruit"]);
        yield return ("RAIN", ["Umbrella", "Cloud", "Puddle", "Wet street"], ["weather"]);
        yield return ("BOOK", ["Library", "Pages", "Study", "Desk"], ["school", "object"]);
    }
}
