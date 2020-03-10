﻿namespace PixelPlanetUtils.CanvasInteraction
{
    public static class PixelMap
    {
        public const int ChunkSideSize = 0x100;
        private const int ChunksPerSide = 0x100;
        public const int ChunkBinarySize = ChunkSideSize * ChunkSideSize;

        public static short ConvertToAbsolute(int chunk, int relative)
        {
            return (short)((chunk - ChunksPerSide / 2) * ChunkSideSize + relative);
        }

        public static void ConvertToRelative(short absolute, out byte chunk, out byte relative)
        {
            int shifted = absolute + ChunksPerSide * ChunkSideSize / 2;
            chunk = (byte)(shifted / ChunkSideSize);
            relative = (byte)(shifted % ChunkSideSize);
        }
    }
}