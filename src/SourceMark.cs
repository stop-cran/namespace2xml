using System;

namespace Namespace2Xml
{
    [Equals]
    public class SourceMark : IComparable<SourceMark>
    {
        public SourceMark(int fileNumber, string fileName, int lineNumber)
        {
            FileNumber = fileNumber;
            FileName = fileName;
            LineNumber = lineNumber;
        }

        public int FileNumber { get; }

        [IgnoreDuringEquals]
        public string FileName { get; }

        public int LineNumber { get; }

        public int CompareTo(SourceMark other)
        {
            var fileDiff = FileNumber.CompareTo(other.FileNumber);

            return fileDiff == 0
                ? LineNumber.CompareTo(other.LineNumber)
                : fileDiff;
        }
    }
}
