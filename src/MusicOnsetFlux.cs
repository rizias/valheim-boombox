using System;

namespace VikingBoombox
{
    internal sealed class MusicOnsetFlux
    {
        private const double PowerFloor = 1e-10;
        private readonly MusicMelBand[] bands;
        private readonly double topDb;
        private readonly float[] mel;
        private float[] current;
        private float[] previous;
        private bool hasPrevious;
        private double loudestDb = double.NegativeInfinity;

        public MusicOnsetFlux(MusicMelBand[] bands, double topDb)
        {
            this.bands = bands;
            this.topDb = topDb;
            mel = new float[bands.Length];
            current = new float[bands.Length];
            previous = new float[bands.Length];
        }

        public void Reset()
        {
            hasPrevious = false;
            loudestDb = double.NegativeInfinity;
        }

        public double Push(float[] power)
        {
            MusicMelBands.Apply(power, bands, mel);
            for (int band = 0; band < mel.Length; band++)
            {
                double db = 10 * Math.Log10(Math.Max(PowerFloor, mel[band]));
                current[band] = (float)db;
                loudestDb = Math.Max(loudestDb, db);
            }
            double floor = loudestDb - topDb, sum = 0;
            if (hasPrevious)
                for (int band = 0; band < current.Length; band++)
                    sum += Math.Max(0, Math.Max(current[band], floor) - Math.Max(NeighbourMax(previous, band), floor));
            hasPrevious = true;
            var swap = previous;
            previous = current;
            current = swap;
            return sum / Math.Max(1, current.Length);
        }

        private static double NeighbourMax(float[] values, int index)
        {
            int last = values.Length - 1;
            return Math.Max(values[Math.Max(0, index - 1)], Math.Max(values[index], values[Math.Min(last, index + 1)]));
        }
    }
}
