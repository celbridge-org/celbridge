using Celbridge.Models.CelMixins;
using System;
using System.Collections.Generic;

namespace Celbridge.Models.CelTypes
{
    public class FileCelType : ICelType
    {
        public string Name => "File";
        public string Description => "Instructions for working with file based data";
        public string Icon => "OpenFile";
        public string Color => "#048181";

        public List<ICelMixin> CelMixins { get; } = new()
        {
            new PrimitivesMixin(),
            new BasicMixin(),
            new FileMixin()
        };
    }
}
