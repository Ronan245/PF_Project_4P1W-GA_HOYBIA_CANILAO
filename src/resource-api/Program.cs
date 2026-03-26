using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using ResourceApi;
using ResourceApi.Security;
using ResourceApi.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = null;
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("default", config =>
    {
        config.PermitLimit = 120;
        config.Window = TimeSpan.FromMinutes(1);
        config.QueueLimit = 0;
    });
});

builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 10 * 1024 * 1024;
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<GameplayOptions>(builder.Configuration.GetSection("Gameplay"));
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddSingleton<ResourceStore>();

var app = builder.Build();

app.UseCors();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseMiddleware<SimpleJwtMiddleware>();

app.MapGet("/", () => Results.Ok(new
{
    service = "resource-api",
    status = "ok"
})).RequireRateLimiting("default");

app.MapGet("/health", () => Results.Ok(new
{
    service = "resource-api",
    status = "healthy",
    timestampUtc = DateTime.UtcNow
})).RequireRateLimiting("default");

app.MapGet("/packs", (HttpContext httpContext, ResourceStore store, bool random = false) =>
{
    var packs = store.GetPublishedPacks(random)
        .Select(pack => pack.ToPackSummary(httpContext))
        .ToList();

    return Results.Ok(packs);
}).RequireRateLimiting("default");

app.MapGet("/puzzles/next", (HttpContext httpContext, ResourceStore store, string packId) =>
{
    var userId = httpContext.User.GetUserId();
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    var result = store.GetNextPuzzle(packId, userId);
    return result is null ? Results.NotFound(new { error = "Pack not found or unavailable." }) : Results.Ok(result.ToResponse(store, httpContext));
}).RequireRateLimiting("default");

app.MapPost("/game/submit", (HttpContext httpContext, ResourceStore store, SubmitGuessRequest request) =>
{
    var userId = httpContext.User.GetUserId();
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    var validationError = request.Validate();
    if (validationError is not null)
    {
        return Results.BadRequest(new { error = validationError });
    }

    var result = store.SubmitGuess(userId, request);
    return result is null ? Results.NotFound(new { error = "Puzzle not found." }) : Results.Ok(result);
}).RequireRateLimiting("default");

app.MapPost("/game/restart", (HttpContext httpContext, ResourceStore store, RestartPackRequest request) =>
{
    var userId = httpContext.User.GetUserId();
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    store.RestartPack(userId, request.PackId);
    return Results.Ok(new { restarted = true });
}).RequireRateLimiting("default");

app.MapGet("/profile/progress", (HttpContext httpContext, ResourceStore store) =>
{
    var userId = httpContext.User.GetUserId();
    if (userId is null)
    {
        return Results.Unauthorized();
    }

    return Results.Ok(store.GetProfileProgress(userId));
}).RequireRateLimiting("default");

var cms = app.MapGroup("/cms").RequireRateLimiting("default");

cms.MapGet("/images", (HttpContext httpContext, ResourceStore store, string? tag) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    var images = store.GetImages(tag).Select(image => image.ToResponse(httpContext)).ToList();
    return Results.Ok(images);
});

cms.MapPost("/images", async (HttpContext httpContext, ResourceStore store) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    if (httpContext.Request.HasFormContentType)
    {
        var form = await httpContext.Request.ReadFormAsync();
        var files = form.Files;
        if (files.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one file is required." });
        }

        var images = await store.AddUploadedImagesAsync(files, form["title"].ToString());
        return Results.Ok(images.Select(image => image.ToResponse(httpContext)));
    }

    var request = await httpContext.Request.ReadFromJsonAsync<CreateImageRequest>();
    if (request is null || string.IsNullOrWhiteSpace(request.Url))
    {
        return Results.BadRequest(new { error = "A valid image URL is required." });
    }

    var image = store.AddUrlImage(request);
    return Results.Ok(image.ToResponse(httpContext));
});

cms.MapPut("/images/{id}", async (HttpContext httpContext, ResourceStore store, string id) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    var request = await httpContext.Request.ReadFromJsonAsync<UpdateImageRequest>();
    if (request is null)
    {
        return Results.BadRequest(new { error = "Request body is required." });
    }

    var image = store.UpdateImage(id, request);
    return image is null ? Results.NotFound() : Results.Ok(image.ToResponse(httpContext));
});

cms.MapDelete("/images/{id}", (HttpContext httpContext, ResourceStore store, string id) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    return store.DeleteImage(id)
        ? Results.NoContent()
        : Results.BadRequest(new { error = "Image is in use by a puzzle or does not exist." });
});

cms.MapGet("/tags", (HttpContext httpContext, ResourceStore store) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    return Results.Ok(store.GetTags());
});

cms.MapPost("/tags", (HttpContext httpContext, ResourceStore store, TagRequest request) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    var tag = store.AddTag(request.Tag);
    return tag is null ? Results.BadRequest(new { error = "Tag is required." }) : Results.Ok(new { tag });
});

cms.MapDelete("/tags/{tag}", (HttpContext httpContext, ResourceStore store, string tag) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    return store.DeleteTag(tag) ? Results.NoContent() : Results.BadRequest(new { error = "Tag is in use or missing." });
});

cms.MapPost("/images/{id}/tags", (HttpContext httpContext, ResourceStore store, string id, TagListRequest request) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    var image = store.AddTagsToImage(id, request.Tags);
    return image is null ? Results.NotFound() : Results.Ok(image.ToResponse(httpContext));
});

cms.MapDelete("/images/{id}/tags/{tag}", (HttpContext httpContext, ResourceStore store, string id, string tag) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    var image = store.RemoveTagFromImage(id, tag);
    return image is null ? Results.NotFound() : Results.Ok(image.ToResponse(httpContext));
});

cms.MapGet("/puzzles", (HttpContext httpContext, ResourceStore store) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    return Results.Ok(store.GetPuzzles().Select(puzzle => puzzle.ToResponse(store, httpContext)));
});

cms.MapPost("/puzzles", (HttpContext httpContext, ResourceStore store, PuzzleUpsertRequest request) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    var result = store.CreatePuzzle(request);
    return result.Error is null
        ? Results.Ok(result.Value!.ToResponse(store, httpContext))
        : Results.BadRequest(new { error = result.Error });
});

cms.MapPut("/puzzles/{id}", (HttpContext httpContext, ResourceStore store, string id, PuzzleUpsertRequest request) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    var result = store.UpdatePuzzle(id, request);
    return result.Error switch
    {
        "not-found" => Results.NotFound(),
        null => Results.Ok(result.Value!.ToResponse(store, httpContext)),
        _ => Results.BadRequest(new { error = result.Error })
    };
});

cms.MapDelete("/puzzles/{id}", (HttpContext httpContext, ResourceStore store, string id) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    return store.DeletePuzzle(id) ? Results.NoContent() : Results.NotFound();
});

cms.MapGet("/packs", (HttpContext httpContext, ResourceStore store) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    return Results.Ok(store.GetAllPacks().Select(pack => pack.ToCmsResponse(store, httpContext)));
});

cms.MapPost("/packs", (HttpContext httpContext, ResourceStore store, PackUpsertRequest request) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    var result = store.CreatePack(request);
    return result.Error is null
        ? Results.Ok(result.Value!.ToCmsResponse(store, httpContext))
        : Results.BadRequest(new { error = result.Error });
});

cms.MapPut("/packs/{id}", (HttpContext httpContext, ResourceStore store, string id, PackUpsertRequest request) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    var result = store.UpdatePack(id, request);
    return result.Error switch
    {
        "not-found" => Results.NotFound(),
        null => Results.Ok(result.Value!.ToCmsResponse(store, httpContext)),
        _ => Results.BadRequest(new { error = result.Error })
    };
});

cms.MapDelete("/packs/{id}", (HttpContext httpContext, ResourceStore store, string id) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    return store.DeletePack(id) ? Results.NoContent() : Results.NotFound();
});

cms.MapPost("/packs/{id}/publish", (HttpContext httpContext, ResourceStore store, string id, PublishPackRequest? request) =>
{
    if (!httpContext.User.IsAdmin())
    {
        return Results.Unauthorized();
    }

    var pack = store.TogglePackPublish(id, request?.Published);
    return pack is null ? Results.NotFound() : Results.Ok(pack.ToCmsResponse(store, httpContext));
});

app.Run();
