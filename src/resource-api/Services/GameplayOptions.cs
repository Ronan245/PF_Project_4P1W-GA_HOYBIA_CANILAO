namespace ResourceApi.Services;

public sealed class GameplayOptions
{
    public int SolvedCooldownHours { get; set; } = 24;

    public int CorrectScore { get; set; } = 10;

    public int IncorrectPenalty { get; set; } = 2;
}

