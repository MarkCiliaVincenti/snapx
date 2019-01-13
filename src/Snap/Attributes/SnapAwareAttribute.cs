﻿using System;
using System.Diagnostics.CodeAnalysis;

namespace Snap.Attributes
{
    [AttributeUsage(AttributeTargets.Assembly)]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public sealed class SnapAwareAttribute : Attribute
    {

    }
}
