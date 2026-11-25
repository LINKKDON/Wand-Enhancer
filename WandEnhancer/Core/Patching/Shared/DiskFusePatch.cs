using System;
using System.IO;
using WandEnhancer.Core;

namespace WandEnhancer.Core.Patching.Shared
{
    /// <summary>Clears the embedded ASAR integrity fuse on disk, or restores it.</summary>
    internal static class DiskFusePatch
    {
        public static bool Apply(string executablePath, Action<string, ELogType> log)
        {
            log = log ?? ((_, __) => { });
            try
            {
                long stateOffset = ElectronFuseWire.FindStateFileOffset(executablePath);
                if (stateOffset < 0)
                {
                    log("[ENHANCER] No Electron fuse block found; the ASAR integrity fuse cannot be cleared.", ELogType.Warn);
                    return false;
                }

                using (var stream = new FileStream(executablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Position = stateOffset;
                    int current = stream.ReadByte();

                    // INHERIT means the fuse was never explicitly set, so it runs
                    // at its default, which for ASAR integrity validation is off.
                    if (current == ElectronFuseWire.StateRemoved || current == ElectronFuseWire.StateDisabled || current == ElectronFuseWire.StateInherit)
                    {
                        log("[ENHANCER] ASAR integrity fuse is already cleared.", ELogType.Info);
                        return true;
                    }

                    if (current != ElectronFuseWire.StateEnabled)
                    {
                        log($"[ENHANCER] ASAR integrity fuse is in an unexpected state (0x{current:X2}); leaving it alone.", ELogType.Warn);
                        return false;
                    }

                    stream.Position = stateOffset;
                    stream.WriteByte(ElectronFuseWire.StateRemoved);
                    stream.Flush();
                }

                log("[ENHANCER] ASAR integrity fuse cleared.", ELogType.Info);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                log($"[ENHANCER] Could not clear the ASAR integrity fuse: {e.Message}", ELogType.Error);
                return false;
            }
        }

        public static void Remove(string executablePath, Action<string, ELogType> log)
        {
            log = log ?? ((_, __) => { });
            try
            {
                long stateOffset = ElectronFuseWire.FindStateFileOffset(executablePath);
                if (stateOffset < 0)
                {
                    return;
                }

                using (var stream = new FileStream(executablePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Position = stateOffset;
                    if (stream.ReadByte() != ElectronFuseWire.StateRemoved)
                    {
                        return;
                    }

                    stream.Position = stateOffset;
                    stream.WriteByte(ElectronFuseWire.StateEnabled);
                    stream.Flush();
                }

                log("[ENHANCER] ASAR integrity fuse restored.", ELogType.Info);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                log($"[ENHANCER] Could not restore the ASAR integrity fuse: {e.Message}", ELogType.Warn);
            }
        }
    }
}
