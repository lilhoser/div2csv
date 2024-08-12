using CsvHelper;
using CsvHelper.Configuration;
using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.XPath;

namespace div2csv
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("usage: div2csv [html file] [spec file] [output file]");
                return;
            }

            var htmlfile = args[0];
            var specfile = args[1];
            var outputFile = args[2];

            Specification specification;

            try
            {
                specification = ParseSpecification(specfile);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Specification is invalid: {ex.Message}");
                return;
            }

            var colCount = specification.GetVisibleColumnCount();
            if (colCount == 0)
            {
                Console.WriteLine("No columns defined in specification.");
                return;
            }

            Console.WriteLine($"Loaded specification with {colCount} columns.");

            var csvContent = GenerateCsvContent(htmlfile, specification);
            if (csvContent == null || csvContent.Rows.Count == 0)
            {
                return;
            }

            Console.WriteLine($"Parsed {csvContent.Rows.Count} records from HTML.");

            try
            {
                var config = new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HasHeaderRecord = true,
                    HeaderValidated = null,
                    Mode = CsvMode.RFC4180,
                    TrimOptions = TrimOptions.Trim,
                };
                using (var writer = new StreamWriter(outputFile))
                using (var csv = new CsvWriter(writer, config))
                {
                    foreach (DataColumn column in csvContent.Columns)
                    {
                        csv.WriteField(column.ColumnName);
                    }
                    csv.NextRecord();

                    foreach (DataRow row in csvContent.Rows)
                    {
                        foreach (DataColumn column in csvContent.Columns)
                        {
                            csv.WriteField(row[column]);
                        }
                        csv.NextRecord();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to save CSV: {ex.Message}");
                return;
            }

            Console.WriteLine($"CSV saved to {outputFile}");
        }

        static Specification ParseSpecification(string SpecFile)
        {
            if (!File.Exists(SpecFile))
            {
                throw new Exception($"Specification file {SpecFile} does not exist.");
            }

            try
            {
                var json = File.ReadAllText(SpecFile);
                var spec = (Specification)JsonConvert.DeserializeObject(json, typeof(Specification))!;
                spec.Validate();
                return spec;
            }
            catch (Exception ex)
            {
                throw new Exception($"Could not deserialize specification: {ex.Message}");
            }
        }

        static DataTable? GenerateCsvContent(string HtmlFile, Specification Specification)
        {
            if (!File.Exists(HtmlFile))
            {
                Console.WriteLine($"HTML file {HtmlFile} does not exist.");
                return null;
            }

            //
            // Column 0 specifies the root HTML element that contains all of the records.
            //
            var rootColumnSpec = Specification.GetColumnSpec("root");
            if (rootColumnSpec == null)
            {
                Console.WriteLine($"Mal-formed specification, root key missing.");
                return null;
            }
            else if (rootColumnSpec.XPaths.Count != 1)
            {
                Console.WriteLine($"Mal-formed specification, root key has multiple xpaths.");
                return null;
            }

            var rootElementXPath = rootColumnSpec.XPaths[0];

            //
            // Build DataTable from columns
            //
            var table = new DataTable("div2csv");
            var columns = Specification.GetVisibleColumnNames();
            columns.ForEach(k => table.Columns.Add(k));

            try
            {
                //
                // Load HTML file
                //
                var doc = new HtmlDocument();
                doc.LoadHtml(File.ReadAllText(HtmlFile));

                //
                // Get root
                //
                var nodes = doc.DocumentNode.SelectNodes(rootElementXPath);
                if (nodes == null || nodes.Count() == 0)
                {
                    Console.WriteLine($"No records found from specified root.");
                    return null;
                }

                //
                // Add a DataTable row for each record and populate columns
                //
                foreach (var node in nodes)
                {
                    var row = table.NewRow();

                    foreach (var column in columns)
                    {
                        var spec = Specification.GetColumnSpec(column);
                        Debug.Assert(spec != null);
                        var xPathList = spec.XPaths; // relative to root
                        var valueFound = false;

                        foreach (var xPath in xPathList) // try each xPath for a value
                        {
                            var columnNode = node.SelectSingleNode(xPath);
                            if (columnNode == null)
                            {
                                continue;
                            }
                            if (columnNode.Name == "a")
                            {
                                //
                                // Preserve the href but strip any contained HTML
                                //
                                var html = columnNode.OuterHtml;
                                var sanitized = SanitizeHtmlContent(columnNode.InnerText, spec.Strip);
                                html = html.Replace(columnNode.InnerHtml, sanitized);
                                row[column] = html;
                            }
                            else
                            {
                                row[column] = SanitizeHtmlContent(columnNode.InnerText, spec.Strip);
                            }
                            valueFound = true;
                            break;
                        }

                        if (!valueFound)
                        {
                            if (spec.Required)
                            {
                                throw new Exception($"Required column '{spec.Name}' is missing in " +
                                    $"node '{node.InnerText}'");
                            }
                            row[column] = "<empty>";
                        }
                    }

                    table.Rows.Add(row);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occurred during parsing: {ex.Message}");
                return null;
            }

            return table;
        }

        static string SanitizeHtmlContent(string Content, List<string> AdditionalStrip)
        {
            var stripped = Regex.Replace(Content, @"<[^>]+>|&nbsp;", ""); // html and encodings
            foreach (var strip in AdditionalStrip)
            {
                stripped = stripped.Replace(strip, "", StringComparison.InvariantCultureIgnoreCase);
            }
            stripped = stripped.ReplaceLineEndings("").Trim();
            return stripped;
        }
    }

    internal class Specification
    {
        [JsonProperty]
        private List<ColumnSpec> ColumnSpecs { get; set; }
        [JsonIgnore]
        public int VisibleColumnCount { get; set; }

        public Specification()
        {
            ColumnSpecs = new List<ColumnSpec>();
        }

        public void AddColumnSpec(ColumnSpec Spec)
        {
            ColumnSpecs.Add(Spec);
        }

        public int GetVisibleColumnCount()
        {
            return ColumnSpecs.Where(c => c.Name.ToLower() != "root").Count();
        }

        public List<string> GetVisibleColumnNames()
        {
            return ColumnSpecs.Where(c => c.Name.ToLower() != "root").Select(
                c => c.Name).ToList();
        }

        public ColumnSpec? GetColumnSpec(string Name)
        {
            return ColumnSpecs.Where(c => c.Name.ToLower() == Name.ToLower()).FirstOrDefault();
        }

        public void Validate()
        {
            if (!ColumnSpecs.Any(s => s.Name.ToLower() == "root"))
            {
                throw new Exception("Column specification lacks a root column");
            }
            else if (ColumnSpecs.Where(s => s.Name.ToLower() == "root").Count() != 1)
            {
                throw new Exception("Root column cannot be repeated");
            }

            foreach (var spec in ColumnSpecs)
            {
                foreach (var xpath in spec.XPaths)
                {
                    try
                    {
                        XPathExpression.Compile(xpath);
                    }
                    catch (XPathException ex)
                    {
                        throw new Exception($"Invalid XPath {xpath}: {ex.Message}");
                    }
                }
            }
        }
    }

    internal class ColumnSpec
    {
        public string Name { get; set; }
        public bool Required { get; set; }
        public List<string> XPaths { get; set; }
        public List<string> Strip { get; set; }

        public ColumnSpec()
        {
            Name = string.Empty;
            Required = false;
            XPaths = new List<string>();
            Strip = new List<string>();
        }
    }
}
