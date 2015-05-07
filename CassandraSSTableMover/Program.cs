using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CassandraSSTableMover
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var directory = Path.GetFullPath(args[0]);
            var targetDirectory = Path.GetFullPath(args[1]);
            var sstableFiles = Directory
                .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Select(x => new
                {
                    Match = regex.Match(x),
                    FileName = x
                })
                .ToArray();
            var notMatched = sstableFiles.Where(x => !x.Match.Success);
            Console.WriteLine("Not matched: ");
            notMatched.Select(x => x.FileName).ToList().ForEach(Console.WriteLine);

            var matched = sstableFiles
                .Where(x => x.Match.Success)
                .Select(x => new
                {
                    Key = x.Match.Groups["sstablekey"].Captures[0].Value, x.FileName
                })
                .ToArray();

            var sstables = matched.GroupBy(x => x.Key).Select(x => new SSTable(x.Select(z => z.FileName).ToArray())).ToArray();
            Console.WriteLine();
            Console.WriteLine("SSTables: ");
            sstables.ToList().ForEach(Console.WriteLine);

            var sstableList = sstables.OrderByDescending(x => x, new SSTableSizeComparer()).ToList();
            var sstableList1 = new List<SSTable>();
            var sstableList2 = new List<SSTable>();

            foreach(var sstable in sstableList)
            {
                if(sstableList1.Select(x => x.Size).Sum() > sstableList2.Select(x => x.Size).Sum())
                    sstableList2.Add(sstable);
                else
                    sstableList1.Add(sstable);
            }
            Console.WriteLine("Bucket1 size: {0}", sstableList1.Select(x => x.Size).Sum());
            Console.WriteLine("Bucket2 size: {0}", sstableList2.Select(x => x.Size).Sum());

            WriteMakeDirectoryStatements(directory, targetDirectory, Console.Out);

            WriteMoveSSTablesStatements(sstableList2, directory, targetDirectory, Console.Out);
        }

        private static void WriteMoveSSTablesStatements(List<SSTable> sstableList, string directory, string targetDirectory, TextWriter output)
        {
            foreach(var fileName in sstableList.SelectMany(x => x.FileNames))
                output.WriteLine(@"move ""{0}"" ""{1}""", fileName, fileName.Replace(directory, targetDirectory));
        }

        private static void WriteMakeDirectoryStatements(string sourceDirectory, string targetDirectory, TextWriter output)
        {
            foreach(var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
                output.WriteLine(@"mkdir ""{0}""", directory.Replace(sourceDirectory, targetDirectory));
        }

        private static readonly Regex regex = new Regex(@"(?<sstablekey>[\w\d\-]+?\-ic\-\d+?)\-.*");
    }

    public class SSTableSizeComparer : IComparer<SSTable>
    {
        public int Compare(SSTable x, SSTable y)
        {
            return x.Size.CompareTo(y.Size);
        }
    }

    public class SSTable
    {
        public SSTable(string[] fileNames)
        {
            this.fileNames = fileNames;
        }

        public override string ToString()
        {
            return string.Format("SSTable: {1} files [{0}]", string.Join(", ", fileNames), fileNames.Length);
        }

        public long Size { get { return size ?? (size = CalculateSize()).Value; } }

        public string[] FileNames { get { return fileNames; } }

        private long CalculateSize()
        {
            return fileNames.Select(x => new FileInfo(x)).Select(x => x.Length).Sum();
        }

        private readonly string[] fileNames;
        private long? size;
    }
}