namespace CalibraHub.Application.Diagnostics.SqlTrace;

/// <summary>
/// Sabit boyutlu halka tampon (ring buffer) — yakalanan izleme kayıtları YALNIZCA burada,
/// süreç belleğinde tutulur. Kullanıcı kararı: parametre değerleri gerçek müşteri verisi
/// taşıyabildiği için (maskelenenler hariç, bkz. SqlTraceMasking) canlı görmek isteniyor ama
/// diske/dosyaya KALICI biriktirilmesi istenmiyor. Süreç yeniden başladığında (veya oturum
/// bitince) tampon tamamen kaybolur — bu bilinçli bir davranıştır, bug değildir.
/// </summary>
public sealed class SqlTraceBuffer
{
    private readonly int _capacity;
    private readonly Queue<SqlTraceEvent> _queue;
    private readonly object _lock = new();
    private long _seq;
    private long _droppedCount;

    public SqlTraceBuffer(int capacity = 2000)
    {
        _capacity = Math.Max(1, capacity);
        _queue = new Queue<SqlTraceEvent>(_capacity);
    }

    /// <summary>Yeni oturum başlarken çağrılır — önceki oturumun kayıtları/sayaçları sıfırlanır.</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _queue.Clear();
            _seq = 0;
            _droppedCount = 0;
        }
    }

    public void Add(SqlTraceEvent template)
    {
        lock (_lock)
        {
            _seq++;
            var withSeq = template with { Seq = _seq };
            _queue.Enqueue(withSeq);
            if (_queue.Count > _capacity)
            {
                _queue.Dequeue();
                _droppedCount++;
            }
        }
    }

    /// <summary>
    /// afterSeq'ten büyük seq'e sahip kayıtları döner. droppedCount, oturum başından beri
    /// tampon taşması nedeniyle kaybolan TOPLAM kayıt sayısıdır (kullanıcı veri kaybettiğini
    /// bilsin diye — sessizce eksik gösterilmez, bkz. görev talimatı).
    /// </summary>
    public (IReadOnlyList<SqlTraceEvent> Events, long DroppedCount) Snapshot(long afterSeq)
    {
        lock (_lock)
        {
            if (_queue.Count == 0) return (Array.Empty<SqlTraceEvent>(), _droppedCount);
            var result = new List<SqlTraceEvent>(_queue.Count);
            foreach (var ev in _queue)
            {
                if (ev.Seq > afterSeq) result.Add(ev);
            }
            return (result, _droppedCount);
        }
    }
}
