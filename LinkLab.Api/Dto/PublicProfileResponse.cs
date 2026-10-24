namespace LinkLab.Api.Dto;

// Only public-facing identity data. Email and account details remain private.
public record PublicProfileResponse(Guid Id, string DisplayName);
