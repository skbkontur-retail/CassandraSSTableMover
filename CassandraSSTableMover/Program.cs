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
            var directory1 = Path.GetFullPath(args[0]);
            var directory2 = Path.GetFullPath(args[1]);
            var targetDirectory = Path.GetFullPath(args[2]);

            var sstableFiles1 =
                Directory
                    .EnumerateFiles(directory1, "*", SearchOption.AllDirectories)
                    .Select(x => new
                    {
                        Match = regex.Match(x),
                        FileName = x
                    })
                    .ToArray();

            var sstableFiles2 =
                Directory
                    .EnumerateFiles(directory2, "*", SearchOption.AllDirectories)
                    .Select(x => new
                    {
                        Match = regex.Match(x),
                        FileName = x
                    })
                    .ToArray();

            var notMatched = sstableFiles1.Where(x => !x.Match.Success);
            Console.WriteLine("Not matched: ");
            notMatched.Select(x => x.FileName).ToList().ForEach(Console.WriteLine);
            notMatched = sstableFiles2.Where(x => !x.Match.Success);
            Console.WriteLine("Not matched: ");
            notMatched.Select(x => x.FileName).ToList().ForEach(Console.WriteLine);

            var matched1 = sstableFiles1
                .Where(x => x.Match.Success)
                .Select(x => new
                {
                    Key = x.Match.Groups["sstablekey"].Captures[0].Value, x.FileName
                })
                .ToArray();
            var matched2 = sstableFiles2
                .Where(x => x.Match.Success)
                .Select(x => new
                {
                    Key = x.Match.Groups["sstablekey"].Captures[0].Value, x.FileName
                })
                .ToArray();

            var sstables1 = matched1.GroupBy(x => x.Key).Select(x => new SSTable(x.Select(z => z.FileName).ToArray())).ToArray();
            var sstables2 = matched2.GroupBy(x => x.Key).Select(x => new SSTable(x.Select(z => z.FileName).ToArray())).ToArray();

            var factor1 = CalculateFactor(sstables1.Sum(x => x.Size), sstables2.Sum(x => x.Size));
            var from1ToTarget = new List<SSTable>();
            var stayIn1 = new List<SSTable>();
            Console.WriteLine("Factor1: {0}", factor1);
            MegaSplit(sstables1, from1ToTarget, stayIn1, factor1);

            var factor2 = CalculateFactor(sstables2.Sum(x => x.Size), sstables1.Sum(x => x.Size));
            Console.WriteLine("Factor2: {0}", factor2);
            var from2ToTarget = new List<SSTable>();
            var stayIn2 = new List<SSTable>();
            MegaSplit(sstables2, from2ToTarget, stayIn2, factor2);


            Console.WriteLine("Source1 size: {0}", stayIn1.Select(x => x.Size).Sum());
            Console.WriteLine("Source2 size: {0}", stayIn2.Select(x => x.Size).Sum());
            Console.WriteLine("Target size: {0}", from1ToTarget.Sum(x => x.Size) + from2ToTarget.Sum(x => x.Size));

            WriteMakeDirectoryStatements(directory1, targetDirectory, Console.Out);
            WriteMakeDirectoryStatements(directory2, targetDirectory, Console.Out);

            WriteMoveSSTablesStatements(from1ToTarget, directory1, targetDirectory, Console.Out);
            WriteMoveSSTablesStatements(from2ToTarget, directory2, targetDirectory, Console.Out);
        }

        private static double CalculateFactor(double x, double y)
        {
            return (x + y) / (2 * x - y);
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

        private static readonly Regex regex = new Regex(@"(?<sstablekey>[\w\d\-]+?\-(ic|jb)\-\d+?)\-.*");

        private static void MegaSplit(SSTable[] source, List<SSTable> target2, List<SSTable> target1, double factor)
        {
            foreach(var sstable in source.OrderByDescending(x => x.Size))
            {
                if(target2.Select(x => x.Size).Sum() == 0 || (target1.Select(x => x.Size).Sum() / (double)target2.Select(x => x.Size).Sum()) > factor)
                    target2.Add(sstable);
                else
                    target1.Add(sstable);
            }

        }
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