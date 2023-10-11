using Celbridge.Utils;
using System;
using System.Collections.Generic;

namespace Celbridge.Models
{
    public interface ICelMixin
    {
        public Dictionary<string, Type> InstructionTypes { get; }
    }
}
