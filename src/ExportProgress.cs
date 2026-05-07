using System;

namespace XKTConversionRevitPlugin
{
    /// <summary>Simple progress reporter passed into extractors.</summary>
    public sealed class ExportProgress : IDisposable
    {
        private int _total;
        private int _done;

        public void Begin(int totalSteps) { _total = totalSteps; _done = 0; }

        public void Advance(string? detail = null)
        {
            _done++;
            if (detail != null && _total > 0)
            {
                int pct = (int)(100.0 * _done / _total);
                Console.Write($"\r  [{pct,3}%] {detail}");
            }
        }

        public void Dispose() { Console.WriteLine(); }
    }
}
