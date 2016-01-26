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
            var directory3 = Path.GetFullPath(args[2]);
            var targetDirectory = Path.GetFullPath(args[3]);

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

            var sstableFiles3 =
                Directory
                    .EnumerateFiles(directory3, "*", SearchOption.AllDirectories)
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

            notMatched = sstableFiles3.Where(x => !x.Match.Success);
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
            var matched3 = sstableFiles3
                .Where(x => x.Match.Success)
                .Select(x => new
                {
                    Key = x.Match.Groups["sstablekey"].Captures[0].Value, x.FileName
                })
                .ToArray();

            var sstables1 = matched1.GroupBy(x => x.Key).Select(x => new SSTable(x.Select(z => z.FileName).ToArray())).ToArray();
            var sstables2 = matched2.GroupBy(x => x.Key).Select(x => new SSTable(x.Select(z => z.FileName).ToArray())).ToArray();
            var sstables3 = matched3.GroupBy(x => x.Key).Select(x => new SSTable(x.Select(z => z.FileName).ToArray())).ToArray();

            var factor1 = CalculateFactor(sstables1.Sum(x => x.Size), sstables2.Sum(x => x.Size), sstables3.Sum(x => x.Size));
            var from1ToTarget = new List<SSTable>();
            var stayIn1 = new List<SSTable>();
            Console.WriteLine("Factor1: {0}", factor1);
            MegaSplit(sstables1, from1ToTarget, stayIn1, factor1);

            var factor2 = CalculateFactor(sstables2.Sum(x => x.Size), sstables1.Sum(x => x.Size), sstables3.Sum(x => x.Size));
            Console.WriteLine("Factor2: {0}", factor2);
            var from2ToTarget = new List<SSTable>();
            var stayIn2 = new List<SSTable>();
            MegaSplit(sstables2, from2ToTarget, stayIn2, factor2);

            var factor3 = CalculateFactor(sstables3.Sum(x => x.Size), sstables2.Sum(x => x.Size), sstables1.Sum(x => x.Size));
            Console.WriteLine("Factor3: {0}", factor3);
            var from3ToTarget = new List<SSTable>();
            var stayIn3 = new List<SSTable>();
            MegaSplit(sstables3, from3ToTarget, stayIn3, factor3);


            Console.WriteLine("Source1 size: {0}", stayIn1.Select(x => x.Size).Sum());
            Console.WriteLine("Source2 size: {0}", stayIn2.Select(x => x.Size).Sum());
            Console.WriteLine("Source3 size: {0}", stayIn3.Select(x => x.Size).Sum());
            Console.WriteLine("Target size: {0}", from1ToTarget.Sum(x => x.Size) + from2ToTarget.Sum(x => x.Size) + from3ToTarget.Sum(x => x.Size));

            WriteMakeDirectoryStatements(directory1, targetDirectory, Console.Out);
            WriteMakeDirectoryStatements(directory2, targetDirectory, Console.Out);
            WriteMakeDirectoryStatements(directory3, targetDirectory, Console.Out);

            WriteMoveSSTablesStatements(from1ToTarget, directory1, targetDirectory, Console.Out);
            WriteMoveSSTablesStatements(from2ToTarget, directory2, targetDirectory, Console.Out);
            WriteMoveSSTablesStatements(from3ToTarget, directory3, targetDirectory, Console.Out);
        }

        private static double CalculateFactor(double x, double y, double z)
        {
            return (x + y + z) / (3 * x - y - z);
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

        static Random _random = new Random();

        static void Shuffle<T>(T[] array)
        {
	        int n = array.Length;
	        for (int i = 0; i < n; i++)
	        {
	            int r = i + (int)(_random.NextDouble() * (n - i));
	            T t = array[r];
	            array[r] = array[i];
	            array[i] = t;
	        }
        }

        private static void MegaSplit(SSTable[] source, List<SSTable> target2, List<SSTable> target1, double factor)
        {
            List<SSTable> tempTarget2 = new List<SSTable>(); 
            List<SSTable > tempTarget1 = new List<SSTable>(); 
            var previousFactor = 100000000.0;
            for (var i = 0; i < 1; i++)
            {
                Shuffle(source);
                foreach(var sstable in source)
                {
                    if(tempTarget2.Select(x => x.Size).Sum() == 0 || (tempTarget1.Select(x => x.Size).Sum() / (double)tempTarget2.Select(x => x.Size).Sum()) > factor)
                        tempTarget2.Add(sstable);
                    else
                        tempTarget1.Add(sstable);
                }
                if (Math.Abs(factor - previousFactor) > Math.Abs(factor - (tempTarget1.Select(x => x.Size).Sum()/(double) tempTarget2.Select(x => x.Size).Sum())))
                {
                    target2.Clear();
                    target2.AddRange(tempTarget2);
                    target1.Clear();
                    target1.AddRange(tempTarget1);
                    previousFactor = (tempTarget1.Select(x => x.Size).Sum()/(double) tempTarget2.Select(x => x.Size).Sum());
                }
                tempTarget2.Clear();
                tempTarget1.Clear();
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