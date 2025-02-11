﻿using System.Diagnostics.CodeAnalysis;

namespace HooahComponents.Utility
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public static class PluginConstant
    {
        public const string GUID = "com.hooh.hooah";
        public const string VERSION = "2.2.0";
#if HS2
        public const string NAME = "Hooah Game Extension (HS2)";
#elif AI
        public const string NAME = "Hooah Game Extension (AI)";
#else
        public const string NAME = "Hooah Game Extension (GENERIC)";
#endif
    }
}
