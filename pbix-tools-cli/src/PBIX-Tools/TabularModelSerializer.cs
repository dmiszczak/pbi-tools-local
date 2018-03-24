﻿using System;
using System.Data.OleDb;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Formatting = Newtonsoft.Json.Formatting;

namespace PbixTools
{
    /// <summary>
    /// Serializes a TMSL database into a <see cref="IProjectFolder"/>.
    /// </summary>
    /// <remarks>Methods for deserialization to be added in a future version.</remarks>
    public class TabularModelSerializer
    {
        private readonly IProjectFolder _folder;

        // Serialize: TMSL (JObject) ==> //*.json|.xml
        //            Expand: database.json (relationships, roles, perspectives)
        //                    dataSources
        //                    tables (columns, hierarchies)
        //                     partitions, measures
        //            Handle non-significant changes (datasourceId)
        //            Replace IDs with static values
        //            Extract Mashup blob (package)
        // Deserialize: Compile 'DataModel' blob

        /* .ids.json
           {
              globalPipe: "",
              dataSources: [],
              tables: [
                partitions: [
                {
                  name, dataSource, location
                }
                ]
              ]
           }
         */

        public TabularModelSerializer(IProjectFolder folder)
        {
            _folder = folder ?? throw new ArgumentNullException(nameof(folder));
        }

        public void Serialize(JObject db)
        {
            var dataSources = db.SelectToken("model.dataSources") as JArray ?? new JArray();
            var projFolder = Path.GetFullPath(Path.Combine(_folder.BasePath, ".."));
            var idCache = new TabularModelIdCache(projFolder, dataSources);

            // 
            // model
            // model.tables []       // special handling of partitions, measures
            // model.dataSources []  // special handling of connectionString

            /* expand: tables
                       table/measures (extract into XML)
                       table/hierarchies -- {name}.json
                       dataSources
                       dataSource/mashup
                       expressions { name, kind=m, expression } ==> {name}.m  [~~not relevant for PBI~~]
            */

            db = ProcessDataSources(db, _folder, idCache);
            db = ProcessTables(db, _folder, idCache);
            // TODO ProcessExpressions()

            SaveDatabase(db, _folder);

            idCache.WriteCacheFile();
        }

        internal static JObject ProcessTables(JObject db, IProjectFolder folder, TabularModelIdCache idCache)
        {
            // hierarchies
            // measures

            if (!(db.SelectToken("model.tables") is JArray tables)) return db;

            // /tables sub-folder
            // remove from db
            // sanitize filenames 

            foreach (var table in tables.OfType<JObject>())
            {
                var name = table["name"]?.Value<string>();
                if (name == null) continue;

                // TODO Come up with a more elegant API
                var _table = ProcessMeasures(table, folder, $@"tables\{name}");
                _table = ProcessHierarchies(_table, folder, $@"tables\{name}");
                _table = ProcessTablePartitions(_table, idCache);

                folder.WriteText($@"tables\{name}\{name}.json", WriteJson(_table));
            }

            db.Value<JObject>("model").Remove("tables");

            return db;

        }

        internal static JObject ProcessMeasures(JObject table, IProjectFolder folder, string pathPrefix)
        {
            var measures = table["measures"]?.Value<JArray>();
            if (measures == null) return table;

            foreach (var measure in measures)
            {
                var name = measure["name"]?.Value<string>();
                if (name == null) continue;

                folder.WriteText(Path.Combine(pathPrefix, "measures", $"{name}.xml"), WriteMeasureXml(measure));
            }

            table = new JObject(table);
            table.Remove("measures");
            return table;
        }

        internal static JObject ProcessHierarchies(JObject table, IProjectFolder folder, string pathPrefix)
        {
            // TODO Remove code duplication (measures, tables, dataSources)
            var hierarchies = table["hierarchies"]?.Value<JArray>();
            if (hierarchies == null) return table;

            foreach (var hierarchy in hierarchies)
            {
                var name = hierarchy["name"]?.Value<string>();
                if (name == null) continue;

                folder.WriteText(Path.Combine(pathPrefix, "hierarchies", $"{name}.json"), WriteJson(hierarchy));
            }

            table = new JObject(table);
            table.Remove("hierarchies");
            return table;
        }

        internal static JObject ProcessTablePartitions(JObject table, TabularModelIdCache idCache)
        {
            var _table = new JObject(table);
            var dataSources = _table.SelectTokens("partitions[*].source.dataSource").OfType<JValue>();

            foreach (var dataSource in dataSources)
            {
                dataSource.Value = idCache.LookupOriginalDataSourceId(dataSource.Value<string>());
            }

            return _table;
        }

        internal static JObject ProcessDataSources(JObject db, IProjectFolder folder, TabularModelIdCache idCache)
        {
            // if Provider=PowerBI, strip out global pipe
            // if mashup, strip out & extract package

            // /tables/{name}/dataSources
            //   ./{name}
            //     /{name}.json  { name, impersonationMode, connectionString:Provider,location }
            //     /mashup
            //       /Formulas/Section1.m ...

            if (!(db.SelectToken("model.dataSources") is JArray dataSources)) return db;

            // /tables sub-folder
            // remove from db
            // sanitize filenames 

            foreach (var dataSource in dataSources.OfType<JObject>())
            {
                var name = dataSource["name"]?.Value<string>();  //TODO replace name using IDCache
                if (name == null) continue;
                var dir = name;

                // connectionString: Global Pipe, Mashup
                var connectionStringToken = dataSource["connectionString"] as JValue;
                var connectionString = connectionStringToken?.Value<string>();
                if (connectionStringToken != null && IsPowerBIConnectionString(connectionString, out var location, out var mashup))
                {
                    // lookup static name
                    name = idCache.LookupOriginalDataSourceId(name); // idCache is traversing via location
                    dataSource["name"] = name;
                    dir = location;
                    // strip values:
                    var bldr = new OleDbConnectionStringBuilder(connectionString);
                    bldr.Remove("global pipe");
                    bldr.Remove("mashup");
                    // keep Provider, Location
                    connectionStringToken.Value = bldr.ConnectionString;

                    var mashupPrefix = Path.Combine(
                        "dataSources",
                        location,
                        "mashup");
                    MashupPackageSerializer.ExtractMashup(folder, mashupPrefix, mashup);
                }

                folder.WriteText($@"dataSources\{dir}\dataSource.json", WriteJson(dataSource));
            }

            db.Value<JObject>("model").Remove("dataSources");

            return db;
        }

        // ReSharper disable once InconsistentNaming
        private static bool IsPowerBIConnectionString(string connectionString, out string location, out string mashup)
        {
            try
            {
                var bldr = new OleDbConnectionStringBuilder(connectionString);
                if (bldr.Provider.Equals("Microsoft.PowerBI.OleDb", StringComparison.InvariantCultureIgnoreCase)
                    && bldr.TryGetValue("mashup", out var _mashup)
                    && bldr.TryGetValue("location", out var _location))
                {
                    location = _location.ToString();
                    mashup = _mashup.ToString();
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // invalid connection string
            }

            location = null;
            mashup = null;
            return false;
        }


        internal static void SaveDatabase(JObject db, IProjectFolder folder)
        {
            folder.WriteText("database.json", WriteJson(db));
        }

        private static Action<TextWriter> WriteJson(JToken json)
        {
            return writer =>
            {
                using (var jWriter = new JsonTextWriter(writer))
                {
                    jWriter.Formatting = Formatting.Indented;
                    json.WriteTo(jWriter);
                }
            };
        }

        private static Action<TextWriter> WriteMeasureXml(JToken json)
        {
            return writer =>
            {
                using (var xml = XmlWriter.Create(writer, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true, WriteEndDocumentOnClose = true }))
                {
                    xml.WriteStartElement("Measure");
                    xml.WriteAttributeString("Name", json.Value<string>("name"));

                    // Handle Expression
                    if (json["expression"] != null)
                    {
                        xml.WriteStartElement("Expression");
                        if (json["expression"] is JArray expr)
                        {
                            xml.WriteCData(String.Join(Environment.NewLine, expr.ToObject<string[]>()));
                        }
                        else
                        {
                            xml.WriteCData(json.Value<string>("expression"));
                        }
                        xml.WriteEndElement();
                    }

                    // Any other properties
                    foreach (var prop in json.Values<JProperty>().Where(p => !(new[] { "name", "expression", "annotations" }).Contains(p.Name)))
                    {
                        xml.WriteStartElement(prop.Name.ToPascalCase());
                        xml.WriteValue(prop.Value.Value<string>());
                        xml.WriteEndElement();
                    }

                    // Annotations
                    if (json["annotations"] is JArray annotations)
                    {
                        foreach (var annotation in annotations)
                        {
                            xml.WriteStartElement("Annotation");
                            xml.WriteAttributeString("Name", annotation.Value<string>("name"));
                            var value = annotation?.Value<string>("value");
                            try
                            {
                                //xml.WriteRaw(XElement.Parse(value).ToString());
                                XElement.Parse(value).WriteTo(xml);
                            }
                            catch (XmlException)
                            {
                                xml.WriteValue(value);
                            }
                            xml.WriteEndElement();
                        }
                    }
                }
            };
        }

        // Handle Data Sources, ID lookups, mashup blobs, Pipe handles
        // .ids.json
        // global.settings -- pbix-tools settings (all)
        // [name].settings -- pbix-tools settings (specific pbix)
        // database.json (model/relationships,model/annotations)
        //   * ignore timestamps (createdTimestamp, lastUpdate, lastSchemaUpdate, lastProcessed, model.modifiedTime, model.structureModifiedTime
        //   * ignore: Annotation["DataTypeAtRefresh"]
        // /dataSources []
        //   /[name]
        //     [name].json -- keep provider, replace global pipe, remove mashup
        //     /mashup
        //      {package}
        // /tables []
        //   /[name] -- must encode invalid characters '/' -> '%2f' (Format: x2)
        //     [name].json (columns, annotations)
        //     /partitions ([name].json)
        //     /measures (XML format??) -- expression: CDATA, annotations['Format'] as xml
        //     /hierarchies
        // /expressions (extract into *.m files)

    }

    public static class XmlExtensions
    {
        public static string ToPascalCase(this string s)
        {
            var sb = new StringBuilder(s);
            if (sb.Length > 0) sb[0] = Char.ToUpper(s[0]);
            return sb.ToString();
        }
    }
}