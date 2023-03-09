using System;

namespace Namespace2Xml.Scheme
{
    public enum EntryType
    {
        root,
        key,
        type,
        filename,
        output,
        delimiter,
        [Obsolete("Used for backward compatibility, use EntryType.delimiter instead.")]
        namespacedelimiter,
        substitute,
        xmloptions
    }
}
