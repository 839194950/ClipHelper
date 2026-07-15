using LocalClipboard.Core.Models;

namespace LocalClipboard.Core.Abstractions;

public interface IHistoryRepository
{
    Task<ClipboardEntry?> GetLatestAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ClipboardEntry>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ClipboardEntry>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken);
    Task InsertAsync(ClipboardEntry entry, CancellationToken cancellationToken);
    Task TouchAsync(Guid id, DateTimeOffset usedAt, CancellationToken cancellationToken);
    Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken);
    Task ClearAsync(bool includeFavorites, CancellationToken cancellationToken);
}
