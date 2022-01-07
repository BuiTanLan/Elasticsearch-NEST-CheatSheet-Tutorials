﻿using Elasticsearch.Net;
using Nest;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinFormsApp1
{
#pragma warning disable S1481 // Unused local variables should be removed
#pragma warning disable S125 // Sections of code should not be commented out
#pragma warning disable S2479 // Whitespace and control characters in string literals should be explicit
    public partial class Form1 : Form
    {
        private ElasticClient elasticClient;

        public Form1()
        {
            InitializeComponent();
            AllocConsole();
            Console.OutputEncoding = Encoding.UTF8;

            CreateElasticClient();
            CreateIndex();
            InsertPosts();
            textBox1_TextChanged(null, null);
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool AllocConsole();

        ~Form1()
        {
            var postIndex = IndexName.From<Product>();
            var deleteIndexResponse = elasticClient.Indices.Delete(postIndex);
        }

        private void CreateElasticClient()
        {
            var node = new Uri("http://192.168.53.224:9200"); //DevSkim: ignore DS137138
            var pool = new SingleNodeConnectionPool(node);
            var settings = new ConnectionSettings(pool);

            //The default index when no index has been explicitly specified and no default indices are specified for the given CLR type
            //settings.DefaultIndex("default-index");
            settings.DefaultMappingFor<Product>(p => p
                .IndexName("my-post-index")
            );
            settings.DefaultFieldNameInferrer(p => p);

            //throw exception instead of checking result.IsValid (except when result.SuccessOrKnownError is false)
            settings.ThrowExceptions();

            //Solve problem with my proxy
            settings.DisableAutomaticProxyDetection();

            settings.EnableDebugMode(callDetails =>
            {
                //Console.WriteLine(callDetails.DebugInformation); //Below code display shorter summary that does not include tcp and threadpool statistics

                Console.WriteLine("Request:");
                Console.WriteLine($"{callDetails.HttpMethod} {callDetails.Uri}");
                if (callDetails.RequestBodyInBytes != null)
                {
                    var json = $"{Encoding.UTF8.GetString(callDetails.RequestBodyInBytes)}";

                    try
                    {
                        using var document = JsonDocument.Parse(json);
                        using var ms = new MemoryStream();
                        using var writer = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
                        document.WriteTo(writer);
                        writer.Flush();
                        json = Encoding.UTF8.GetString(ms.ToArray());
                    }
                    catch { }

                    Console.WriteLine(json);
                }

                Console.WriteLine();

                Console.WriteLine("Response:");
                Console.WriteLine($"Status: {callDetails.HttpStatusCode}");
                if (callDetails.ResponseBodyInBytes != null)
                    Console.WriteLine($"{Encoding.UTF8.GetString(callDetails.ResponseBodyInBytes)}");

                Console.WriteLine($"{new string('-', 30)}\n");
                Console.WriteLine();
            });

            elasticClient = new ElasticClient(settings);
        }

        private void CreateIndex()
        {
            var postIndex = IndexName.From<Product>();

            #region Default Analyzer
            //var aaa = elasticClient.Indices.UpdateSettings(postIndex, p => p
            //    .IndexSettings(p => p
            //        .Analysis(p => p
            //            .Analyzers(p => p
            //                .UserDefined("default", new SimpleAnalyzer())
            //            )
            //        )
            //    )
            //);
            //Request:
            //PUT http://localhost:9200/my-post-index/_settings?pretty=true&error_trace=true
            //{
            //  "analysis": {
            //    "analyzer": {
            //      "default": {
            //        "type": "simple"
            //      }
            //    }
            //  }
            //}

            //var bbb = elasticClient.Indices.Create(postIndex, p => p
            //    .Settings(p => p
            //        .Analysis(p => p
            //            .Analyzers(p => p
            //                .UserDefined("default", new SimpleAnalyzer())
            //            )
            //        )
            //    )
            //);
            //Request:
            //PUT http://localhost:9200/my-post-index?pretty=true&error_trace=true
            //{
            //  "settings": {
            //    "analysis": {
            //      "analyzer": {
            //        "default": {
            //          "type": "simple"
            //        }
            //      }
            //    }
            //  }
            //}
            #endregion

            //### Delete
            var deleteIndexResponse = elasticClient.Indices.Delete(postIndex);

            //### Create Index
            var stopwordsPath = Path.Combine(Directory.GetCurrentDirectory(), "stopwords.txt");

            //var stopwords = File.ReadAllLines(stopwordsPath).Select(p => p.Trim());
            var stopwords = new[] {
            "جامع",
            "آرام",
            "میرود",
            "کردند",
            "123"};

            #region Persian & Arabic Analyzer/Normalizer
            //Persian Chars
            //Normalizer: https://github.com/apache/lucenenet/blob/master/src/Lucene.Net.Analysis.Common/Analysis/Fa/PersianNormalizer.cs
            //YEH_FARSI	        (char)1740 == '\u06CC'	    'ی'
            //YEH		        (char)1610 == '\u064A'	    'ي'
            //YEH_BARREE	    (char)1746 == '\u06D2'	    'ے'
            //HEH (farsi)	    (char)1607 == '\u0647'	    'ه'
            //HEH_YEH		    (char)1728 == '\u06C0'	    'ۀ'
            //HEH_GOAL	        (char)1729 == '\u06C1'	    'ہ'
            //KEHEH 		    (char)1705 == '\u06A9'	    'ک'
            //KAF		        (char)1603 == '\u0643'	    'ك'
            //HAMZA_ABOVE       (char)1620 == '\u0654'	    'ٔ'
            //ZERO_SPACE        (char)8204 == '\u200C'      '‌'
            //NORMAL_SPACE      (char)32   == '\u0020'      ' '

            //Persian Fixing
            //YEH		        "\\u064A=>\\u06CC"          'ي' => 'ی'
            //YEH_BARREE	    "\\u06D2=>\\u06CC"          'ے' => 'ی'
            //KAF		        "\\u0643=>\\u06A9"          'ك' => 'ک'
            //HEH_YEH	        "\\u06C0=>\\u0647"          'ۀ' => 'ه'
            //HEH_GOAL	        "\\u06C1=>\\u0647"          'ہ' => 'ه'
            //HAMZA_ABOVE       REMOVE "\\u0654=>"          'ٔ'

            //Arabic Chars (except persians)
            //Normalizer: https://github.com/apache/lucenenet/blob/master/src/Lucene.Net.Analysis.Common/Analysis/Ar/ArabicNormalizer.cs
            //ALEF              (char)1575 == '\u0627'      'ا'
            //ALEF_MADDA        (char)1570 == '\u0622'      'آ'
            //ALEF_HAMZA_ABOVE  (char)1571 == '\u0623'      'أ'
            //ALEF_HAMZA_BELOW  (char)1573 == '\u0625'      'إ'
            //DOTLESS_YEH       (char)1609 == '\u0649'      'ى'
            //TEH_MARBUTA       (char)1577 == '\u0629'      'ة'
            //TATWEEL           (char)1600 == '\u0640'      'ـ' (KhateTire)
            //FATHA             (char)1614 == '\u064E'      'َ' (Fathe)
            //FATHATAN          (char)1611 == '\u064B'      'ً' (TanvinFathe)
            //DAMMA             (char)1615 == '\u064F'      'ُ' (Zamme)
            //DAMMATAN          (char)1612 == '\u064C'      'ٌ' (TanvinZamme)
            //KASRA             (char)1616 == '\u0650'      'ِ' (Kasre)
            //KASRATAN          (char)1613 == '\u064D'      'ٍ' (TanvinKasre)
            //SHADDA            (char)1617 == '\u0651'      'ّ' (Tashdid)
            //SUKUN             (char)1618 == '\u0652'      'ْ' (Sokun)

            //Arabic Fixing
            //ALEF_MADDA        "\\u0622=>\\u0627"          'آ' => 'ا'
            //ALEF_HAMZA_ABOVE  "\\u0623=>\\u0627"          'أ' => 'ا'
            //ALEF_HAMZA_BELOW  "\\u0625=>\\u0627"          'إ' => 'ا'
            //DOTLESS_YEH       "\\u0649=>\\u06CC"          'ى' => 'ی'  (original normalizer replaces with \u064A 'ي')
            //TEH_MARBUTA       "\\u0629=>\\u0647"          'ة' => 'ه'
            //TATWEEL           REMOVE "\\u0640=>"          'ـ' (KhateTire)
            //FATHA             REMOVE "\\u064E=>"          'َ' (Fathe)
            //FATHATAN          REMOVE "\\u064B=>"          'ً' (TanvinFathe)
            //DAMMA             REMOVE "\\u064F=>"          'ُ' (Zamme)
            //DAMMATAN          REMOVE "\\u064C=>"          'ٌ' (TanvinZamme)
            //KASRA             REMOVE "\\u0650=>"          'ِ' (Kasre)
            //KASRATAN          REMOVE "\\u064D=>"          'ٍ' (TanvinKasre)
            //SHADDA            REMOVE "\\u0651=>"          'ّ' (Tashdid)
            //SUKUN             REMOVE "\\u0652=>"          'ْ' (Sokun)

            //Arab Ameri Chars
            //Normalizer: https://github.com/SalmanAA/Lucene.Net.Analysis.Fa/blob/master/Lucene.Net.Analysis.Fa/PersianNormalizer.cs
            //Stemer    : https://github.com/SalmanAA/Lucene.Net.Analysis.Fa/blob/master/Lucene.Net.Analysis.Fa/PersianStemmer.cs
            //HAMZE_JODA        (char)1569 == '\u0621'      'ء' (HamzeJoda)

            //Arab Ameri Fixing
            //HAMZE_JODA        REMOVE "\\u0621=>"          'ء' (HamzeJoda)

            //var createIndexResponse = elasticClient.Indices.Create(postIndex, p => p
            //    .Settings(p => p
            //        .Analysis(p => p
            //            .CharFilters(p => p
            //                //https://www.elastic.co/guide/en/elasticsearch/reference/current/analysis-mapping-charfilter.html
            //                .Mapping("mapping_filter", p => p
            //                    .Mappings(
            //                        "\\u200C=>\\u0020"//, //Fix ZERO_SPACE
            //                                          //"\\u064A=>\\u06CC", //Fix YEH
            //                                          //"\\u06D2=>\\u06CC", //Fix YEH_BARREE
            //                                          //"\\u0649=>\\u06CC", //Fix DOTLESS_YEH
            //                                          //"\\u0643=>\\u06A9", //Fix KAF
            //                                          //"\\u06C0=>\\u0647", //Fix HEH_YEH
            //                                          //"\\u06C1=>\\u0647", //Fix HEH_GOAL
            //                                          //"\\u0629=>\\u0647", //Fix TEH_MARBUTA
            //                                          //"\\u0622=>\\u0627", //Fix ALEF_MADDA
            //                                          //"\\u0623=>\\u0627", //Fix ALEF_HAMZA_ABOVE
            //                                          //"\\u0625=>\\u0627", //Fix ALEF_HAMZA_BELOW
            //                                          //"\\u0654=>",        //Remove HAMZA_ABOVE
            //                                          //"\\u0640=>",        //Remove TATWEEL 
            //                                          //"\\u064E=>",        //Remove FATHA   
            //                                          //"\\u064B=>",        //Remove FATHATAN
            //                                          //"\\u064F=>",        //Remove DAMMA   
            //                                          //"\\u064C=>",        //Remove DAMMATAN
            //                                          //"\\u0650=>",        //Remove KASRA   
            //                                          //"\\u064D=>",        //Remove KASRATAN
            //                                          //"\\u0651=>",        //Remove SHADDA  
            //                                          //"\\u0652=>"         //Remove SUKUN   
            //                                          //"\\u0621=>"         //Remove HAMZE_JODA   
            //                    )
            //                )
            //            )
            //            .TokenFilters(p => p
            //                //https://www.elastic.co/guide/en/elasticsearch/reference/current/analysis-stop-tokenfilter.html
            //                .Stop("persian_stop", p => p
            //                    .StopWords(stopwords)
            //                    .RemoveTrailing()
            //                //.IgnoreCase()
            //                )
            //                .PatternReplace("fix-YEH", p => p.Pattern("\u064A").Replacement("\u06CC"))
            //                .PatternReplace("fix-YEH_BARREE", p => p.Pattern("\u06D2").Replacement("\u06CC"))
            //                .PatternReplace("fix-DOTLESS_YEH", p => p.Pattern("\u0649").Replacement("\u06CC"))
            //                .PatternReplace("fix-KAF", p => p.Pattern("\u0643").Replacement("\u06A9"))
            //                .PatternReplace("fix-HEH_YEH", p => p.Pattern("\u06C0").Replacement("\u0647"))
            //                .PatternReplace("fix-HEH_GOAL", p => p.Pattern("\u06C1").Replacement("\u0647"))
            //                .PatternReplace("fix-TEH_MARBUTA", p => p.Pattern("\u0629").Replacement("\u0647"))
            //                .PatternReplace("fix-ALEF_MADDA", p => p.Pattern("\u0622").Replacement("\u0627"))
            //                .PatternReplace("fix-ALEF_HAMZA_ABOVE", p => p.Pattern("\u0623").Replacement("\u0627"))
            //                .PatternReplace("fix-ALEF_HAMZA_BELOW", p => p.Pattern("\u0625").Replacement("\u0627"))
            //                .PatternReplace("remove-HAMZA_ABOVE", p => p.Pattern("\u0654").Replacement(string.Empty))
            //                .PatternReplace("remove-TATWEEL", p => p.Pattern("\u0640").Replacement(string.Empty))
            //                .PatternReplace("remove-FATHA", p => p.Pattern("\u064E").Replacement(string.Empty))
            //                .PatternReplace("remove-FATHATAN", p => p.Pattern("\u064B").Replacement(string.Empty))
            //                .PatternReplace("remove-DAMMA", p => p.Pattern("\u064F").Replacement(string.Empty))
            //                .PatternReplace("remove-DAMMATAN", p => p.Pattern("\u064C").Replacement(string.Empty))
            //                .PatternReplace("remove-KASRA", p => p.Pattern("\u0650").Replacement(string.Empty))
            //                .PatternReplace("remove-KASRATAN", p => p.Pattern("\u064D").Replacement(string.Empty))
            //                .PatternReplace("remove-SHADDA", p => p.Pattern("\u0651").Replacement(string.Empty))
            //                .PatternReplace("remove-SUKUN", p => p.Pattern("\u0652").Replacement(string.Empty))
            //                .PatternReplace("remove-HAMZE_JODA", p => p.Pattern("\u0621").Replacement(string.Empty))
            //            )
            //            .Analyzers(p => p
            //                .Custom("persian_analyzer", p => p
            //                    .Tokenizer("standard") //https://www.elastic.co/guide/en/elasticsearch/reference/current/analysis-standard-tokenizer.html
            //                    .CharFilters(
            //                        "html_strip", //https://www.elastic.co/guide/en/elasticsearch/reference/current/analysis-htmlstrip-charfilter.html
            //                        "mapping_filter"
            //                    )
            //                    .Filters(
            //                        "lowercase", //https://www.elastic.co/guide/en/elasticsearch/reference/current/analysis-lowercase-tokenfilter.html
            //                        "decimal_digit", //https://www.elastic.co/guide/en/elasticsearch/reference/current/analysis-decimal-digit-tokenfilter.html
            //                        "persian_stop",

            //                        //"arabic_normalization", //https://lucene.apache.org/core/8_8_0/analyzers-common/org/apache/lucene/analysis/ar/ArabicNormalizer.html
            //                        //"persian_normalization", //https://lucene.apache.org/core/8_8_0/analyzers-common/org/apache/lucene/analysis/fa/PersianNormalizer.html
            //                        "fix-YEH",
            //                        "fix-YEH_BARREE",
            //                        "fix-DOTLESS_YEH",
            //                        "fix-KAF",
            //                        "fix-HEH_YEH",
            //                        "fix-HEH_GOAL",
            //                        "fix-TEH_MARBUTA",
            //                        "fix-ALEF_MADDA",
            //                        "fix-ALEF_HAMZA_ABOVE",
            //                        "fix-ALEF_HAMZA_BELOW",
            //                        "remove-HAMZA_ABOVE",
            //                        "remove-TATWEEL",
            //                        "remove-FATHA",
            //                        "remove-FATHATAN",
            //                        "remove-DAMMA",
            //                        "remove-DAMMATAN",
            //                        "remove-KASRA",
            //                        "remove-KASRATAN",
            //                        "remove-SHADDA",
            //                        "remove-SUKUN",
            //                        "remove-SUKUN",
            //                        "remove-HAMZE_JODA",

            //                        "persian_stop" //remove stopwords before and after normalizations because of ('آ' => 'ا')
            //                    )
            //                )
            //            )
            //        )
            //    )
            //    .Map<Post>(p => p
            //        .AutoMap()
            //        .Properties(p => p
            //            //.Number(p => p.Name(p => p.Id))
            //            .Text(p => p
            //                .Name(p => p.Title)
            //                .Analyzer("persian_analyzer")
            //            )
            //            .Text(p => p
            //                .Name(p => p.Body)
            //                .Analyzer("persian_analyzer")
            //            //.SearchAnalyzer("persian_analyzer")
            //            //.Boost(1.5)
            //            //.Store(true)
            //            //.Index(true)
            //            //.Norms(true)
            //            //.IndexPhrases(true)
            //            //.IndexOptions(IndexOptions.Offsets)
            //            //.TermVector(TermVectorOption.WithPositionsOffsetsPayloads)
            //            )
            //        )
            //    )
            //);
            #endregion

            var createIndexResponse = elasticClient.Indices.Create(postIndex, p => p
            #region Text Analyzers
            #region Default (fastest - 110ms)
                //.Settings(p => p
                //    .Analysis(p => p
                //        .CharFilters(p => p
                //            .Mapping("mapping_filter", p => p
                //                .Mappings(
                //                    "\\u200C=>\\u0020" //Fix ZERO_SPACE
                //                )
                //            )
                //        )
                //        .TokenFilters(p => p
                //            .Stop("persian_stop", p => p
                //                .StopWords("تست")
                //                .RemoveTrailing()
                //            )
                //        )
                //        .Analyzers(p => p
                //            .Custom("persian_analyzer", p => p
                //                .Tokenizer("standard")
                //                .CharFilters(
                //                    "html_strip",
                //                    "mapping_filter"
                //                )
                //                .Filters(
                //                    "lowercase",
                //                    "decimal_digit",
                //                    "persian_stop",
                //                    "arabic_normalization",
                //                    "persian_normalization",
                //                    "persian_stop"
                //                )
                //            )
                //        )
                //    )
                //)
            #endregion
            #region MappingFilter (same as below - 145ms)
                //.Settings(p => p
                //    .Analysis(p => p
                //        .CharFilters(p => p
                //            .Mapping("mapping_filter", p => p
                //                .Mappings(
                //                    "\\u200C=>\\u0020",   //Fix ZERO_SPACE
                //                    "\\u064A=>\\u06CC",   //Fix YEH
                //                    "\\u06D2=>\\u06CC",   //Fix YEH_BARREE
                //                    "\\u0649=>\\u06CC",   //Fix DOTLESS_YEH
                //                    "\\u0643=>\\u06A9",   //Fix KAF
                //                    "\\u06C0=>\\u0647",   //Fix HEH_YEH
                //                    "\\u06C1=>\\u0647",   //Fix HEH_GOAL
                //                    "\\u0629=>\\u0647",   //Fix TEH_MARBUTA
                //                    "\\u0622=>\\u0627",   //Fix ALEF_MADDA
                //                    "\\u0623=>\\u0627",   //Fix ALEF_HAMZA_ABOVE
                //                    "\\u0625=>\\u0627",   //Fix ALEF_HAMZA_BELOW
                //                    "\\u0654=>",          //Remove HAMZA_ABOVE
                //                    "\\u0640=>",          //Remove TATWEEL 
                //                    "\\u064E=>",          //Remove FATHA   
                //                    "\\u064B=>",          //Remove FATHATAN
                //                    "\\u064F=>",          //Remove DAMMA   
                //                    "\\u064C=>",          //Remove DAMMATAN
                //                    "\\u0650=>",          //Remove KASRA   
                //                    "\\u064D=>",          //Remove KASRATAN
                //                    "\\u0651=>",          //Remove SHADDA  
                //                    "\\u0652=>",          //Remove SUKUN   
                //                    "\\u0621=>"           //Remove HAMZE_JODA   
                //                )
                //            )
                //        )
                //        .TokenFilters(p => p
                //            .Stop("persian_stop", p => p
                //                .StopWords("تست")
                //                .RemoveTrailing()
                //            )
                //        )
                //        .Analyzers(p => p
                //            .Custom("persian_analyzer", p => p
                //                .Tokenizer("standard")
                //                .CharFilters(
                //                    "html_strip",
                //                    "mapping_filter"
                //                )
                //                .Filters(
                //                    "lowercase",
                //                    "decimal_digit",
                //                    "persian_stop"
                //                )
                //            )
                //        )
                //    )
                //)
            #endregion
            #region PatternReplace (same as above - 145ms - prefered)
                //.Settings(p => p
                //    .Analysis(p => p
                //        .CharFilters(p => p
                //            .Mapping("mapping_filter", p => p
                //                .Mappings(
                //                    "\\u200C=>\\u0020"    //Fix ZERO_SPACE 
                //                )
                //            )
                //        )
                //        .TokenFilters(p => p
                //            .Stop("persian_stop", p => p
                //                .StopWords("تست")
                //                .RemoveTrailing()
                //            )
                //            .PatternReplace("fix-YEH", p => p.Pattern("\u064A").Replacement("\u06CC"))
                //            .PatternReplace("fix-YEH_BARREE", p => p.Pattern("\u06D2").Replacement("\u06CC"))
                //            .PatternReplace("fix-DOTLESS_YEH", p => p.Pattern("\u0649").Replacement("\u06CC"))
                //            .PatternReplace("fix-KAF", p => p.Pattern("\u0643").Replacement("\u06A9"))
                //            .PatternReplace("fix-HEH_YEH", p => p.Pattern("\u06C0").Replacement("\u0647"))
                //            .PatternReplace("fix-HEH_GOAL", p => p.Pattern("\u06C1").Replacement("\u0647"))
                //            .PatternReplace("fix-TEH_MARBUTA", p => p.Pattern("\u0629").Replacement("\u0647"))
                //            .PatternReplace("fix-ALEF_MADDA", p => p.Pattern("\u0622").Replacement("\u0627"))
                //            .PatternReplace("fix-ALEF_HAMZA_ABOVE", p => p.Pattern("\u0623").Replacement("\u0627"))
                //            .PatternReplace("fix-ALEF_HAMZA_BELOW", p => p.Pattern("\u0625").Replacement("\u0627"))
                //            .PatternReplace("remove-HAMZA_ABOVE", p => p.Pattern("\u0654").Replacement(string.Empty))
                //            .PatternReplace("remove-TATWEEL", p => p.Pattern("\u0640").Replacement(string.Empty))
                //            .PatternReplace("remove-FATHA", p => p.Pattern("\u064E").Replacement(string.Empty))
                //            .PatternReplace("remove-FATHATAN", p => p.Pattern("\u064B").Replacement(string.Empty))
                //            .PatternReplace("remove-DAMMA", p => p.Pattern("\u064F").Replacement(string.Empty))
                //            .PatternReplace("remove-DAMMATAN", p => p.Pattern("\u064C").Replacement(string.Empty))
                //            .PatternReplace("remove-KASRA", p => p.Pattern("\u0650").Replacement(string.Empty))
                //            .PatternReplace("remove-KASRATAN", p => p.Pattern("\u064D").Replacement(string.Empty))
                //            .PatternReplace("remove-SHADDA", p => p.Pattern("\u0651").Replacement(string.Empty))
                //            .PatternReplace("remove-SUKUN", p => p.Pattern("\u0652").Replacement(string.Empty))
                //            .PatternReplace("remove-HAMZE_JODA", p => p.Pattern("\u0621").Replacement(string.Empty))
                //        )
                //        .Analyzers(p => p
                //            .Custom("persian_analyzer", p => p
                //                .Tokenizer("standard")
                //                .CharFilters(
                //                    "html_strip",
                //                    "mapping_filter"
                //                )
                //                .Filters(
                //                    "lowercase",
                //                    "decimal_digit",
                //                    "persian_stop",
                //                    "fix-YEH",
                //                    "fix-YEH_BARREE",
                //                    "fix-DOTLESS_YEH",
                //                    "fix-KAF",
                //                    "fix-HEH_YEH",
                //                    "fix-HEH_GOAL",
                //                    "fix-TEH_MARBUTA",
                //                    "fix-ALEF_MADDA",
                //                    "fix-ALEF_HAMZA_ABOVE",
                //                    "fix-ALEF_HAMZA_BELOW",
                //                    "remove-HAMZA_ABOVE",
                //                    "remove-TATWEEL",
                //                    "remove-FATHA",
                //                    "remove-FATHATAN",
                //                    "remove-DAMMA",
                //                    "remove-DAMMATAN",
                //                    "remove-KASRA",
                //                    "remove-KASRATAN",
                //                    "remove-SHADDA",
                //                    "remove-SUKUN",
                //                    "remove-SUKUN",
                //                    "remove-HAMZE_JODA",
                //                    "persian_stop"
                //                )
                //            )
                //        )
                //    )
                //)
            #endregion
            #endregion
                .Map<Product>(p => p
                    .AutoMap()
                #region Nested
                .Properties(p => p
                    .Text(p => p
                        .Name(p => p.Description)
                        .Analyzer("persian_analyzer")
                    )
                //  .Nested<SKUCollection>(p => p
                //      .AutoMap()
                //      .Name(p => p.SKUCollections)
                //  )
                //  .Nested<ProductAttributeCombination>(p => p
                //      .AutoMap()
                //      .Name(p => p.ProductAttributeCombinations)
                //      .Properties(p => p
                //          .Nested<SKUCollection>(p => p
                //              .AutoMap()
                //              .Name(p => p.SKUCollections)
                //          )
                //      )
                //  )
                )
                #endregion
                #region Text Analyzers
                //.Properties(p => p
                //    .Text(p => p
                //        .Name(p => p.Name)
                //        .Analyzer("persian_analyzer")
                //    //.Boost(1.5)
                //    )
                //    .Text(p => p
                //        .Name(p => p.Body)
                //        .Analyzer("persian_analyzer")
                //    //.Boost(1.0)
                //    //.Store(true)
                //    //.Index(true)
                //    //.Norms(true)
                //    //.IndexPhrases(true)
                //    //.IndexOptions(IndexOptions.Offsets)
                //    //.TermVector(TermVectorOption.WithPositionsOffsetsPayloads)
                //    )
                //)
                #endregion
                )
            );

            #region Mappings Product (default)
            //{
            //  "mappings": {
            //    "_doc": {
            //      "properties": {
            //        "Id": {
            //          "type": "integer"
            //        },
            //        "InsertDate": {
            //          "type": "date"
            //        },
            //        "LikeCount": {
            //          "type": "integer"
            //        },
            //        "Name": {
            //          "type": "text",
            //          "fields": {
            //            "keyword": {
            //              "type": "keyword",
            //              "ignore_above": 256
            //            }
            //          }
            //        },
            //        "ProductAttributeCombinations": {
            //          "properties": {
            //            "Name": {
            //              "type": "text",
            //              "fields": {
            //                "keyword": {
            //                  "type": "keyword",
            //                  "ignore_above": 256
            //                }
            //              }
            //            },
            //            "SKUCollections": {
            //              "properties": {
            //                "CollectionId": {
            //                  "type": "text",
            //                  "fields": {
            //                    "keyword": {
            //                      "type": "keyword",
            //                      "ignore_above": 256
            //                    }
            //                  }
            //                },
            //                "InsertDate": {
            //                  "type": "date"
            //                },
            //                "IsFeaturedProduct": {
            //                  "type": "boolean"
            //                },
            //                "LikeCount": {
            //                  "type": "integer"
            //                },
            //                "Name": {
            //                  "type": "text",
            //                  "fields": {
            //                    "keyword": {
            //                      "type": "keyword",
            //                      "ignore_above": 256
            //                    }
            //                  }
            //                }
            //              }
            //            }
            //          }
            //        },
            //        "SKUCollections": {
            //          "properties": {
            //            "CollectionId": {
            //              "type": "text",
            //              "fields": {
            //                "keyword": {
            //                  "type": "keyword",
            //                  "ignore_above": 256
            //                }
            //              }
            //            },
            //            "InsertDate": {
            //              "type": "date"
            //            },
            //            "IsFeaturedProduct": {
            //              "type": "boolean"
            //            },
            //            "LikeCount": {
            //              "type": "integer"
            //            },
            //            "Name": {
            //              "type": "text",
            //              "fields": {
            //                "keyword": {
            //                  "type": "keyword",
            //                  "ignore_above": 256
            //                }
            //              }
            //            }
            //          }
            //        }
            //      }
            //    }
            //  }
            //}
            #endregion

            #region Mappings Post
            //Request:
            //PUT /my-post-index?pretty=true&error_trace=true
            //{
            //  "mappings": {
            //    "properties": {
            //      "id": {
            //        "type": "integer"
            //      },
            //      "title": {
            //        "analyzer": "persian_analyzer",
            //        "boost": 1.5,
            //        "type": "text"
            //      },
            //      "body": {
            //        "analyzer": "persian_analyzer",
            //        "boost": 1.0,
            //        "type": "text"
            //      }
            //    }
            //  },
            //  "settings": {
            //    "analysis": {
            //      "analyzer": {
            //        "persian_analyzer": {
            //          "char_filter": [
            //            "html_strip",
            //            "mapping_filter"
            //          ],
            //          "filter": [
            //            "lowercase",
            //            "decimal_digit",
            //            "arabic_normalization",
            //            "persian_normalization",
            //            "persian_stop"
            //          ],
            //          "tokenizer": "standard",
            //          "type": "custom"
            //        }
            //      },
            //      "char_filter": {
            //        "mapping_filter": {
            //          "mappings": [
            //            "\\u200C=>\\u0020"
            //          ],
            //          "type": "mapping"
            //        }
            //      },
            //      "filter": {
            //        "persian_stop": {
            //          "remove_trailing": true,
            //          "stopwords": "_persian_",
            //          "type": "stop"
            //        }
            //      }
            //    }
            //  }
            //}
            #endregion
        }

        private void InsertPosts()
        {
            var minDate = new DateTime(2021, 01, 01);
            var maxDate = new DateTime(2021, 01, 02);

            var posts = new Product[]
            {
                #region Check Persian Analyzers
                //new() { Id = 1, Description = "متن تست" },
                //new() { Id = 2, Description = "متن تستی" },
                //new() { Id = 3, Description = "متن‌آموزشی" },
                //new() { Id = 4, Description = "كتاب عربی" },
                //new() { Id = 5, Description = "کتاب فارسی" },
                //new() { Id = 6, Description = "ياقوت  عربی" },
                //new() { Id = 7, Description = "یاقوت  فارسی" },
                //new() { Id = 8, Description = "مہراب عربی" },
                //new() { Id = 9, Description = "مهراب فارسی" },
                //new() { Id = 10, Description = "اعداد انگلیسی 1 2 3 4 5 6 7 8 9 0" },
                //new() { Id = 11, Description = "اعداد فارسی ۱ ۲ ۳ ۴ ۵ ۶ ۷ ۸ ۹ ۰" },
                //new() { Id = 12, Description = "اعداد انگلیسی ١ ٢ ٣ ٤ ٥ ٦ ٧ ٨ ٩ ٠" },
	            #endregion

                #region MyRegion
                //new()
                //{
                //    Id = 1,
                //    Name = "Product 1",
                //    InsertDate = null,
                //    LikeCount = null,
                //    SKUCollections = new()
                //    {
                //        new()
                //        {
                //            Name = "Product 1 - SKUCollection 1",
                //            CollectionId = "P1_SKU1",
                //            IsFeaturedProduct = true,
                //            InsertDate = minDate,
                //            LikeCount = 1
                //        },
                //        new()
                //        {
                //            Name = "Product 1 - SKUCollection 2",
                //            CollectionId = "P1_SKU2",
                //            IsFeaturedProduct = false,
                //            InsertDate = maxDate,
                //            LikeCount = 2
                //        }
                //    },
                //    ProductAttributeCombinations = new()
                //    {
                //        new()
                //        {
                //            Name = "Product 1 - PAC 1",
                //            SKUCollections = new()
                //            {
                //                new()
                //                {
                //                    Name = "Product 1 - PAC1 - SKUCollection 1",
                //                    CollectionId = "P1_PAC1_SKU1",
                //                    IsFeaturedProduct = true,
                //                    InsertDate = minDate,
                //                    LikeCount = 1
                //                },
                //                new()
                //                {
                //                    Name = "Product 1 - PAC1 - SKUCollection 2",
                //                    CollectionId = "P1_PAC1_SKU2",
                //                    IsFeaturedProduct = false,
                //                    InsertDate = maxDate,
                //                    LikeCount = 2
                //                }
                //            }
                //        },
                //        new()
                //        {
                //            Name = "Product 1 - PAC 2",
                //            SKUCollections = new()
                //            {
                //                new()
                //                {
                //                    Name = "Product 1 - PAC2 - SKUCollection 1",
                //                    CollectionId = "P1_PAC2_SKU1",
                //                    IsFeaturedProduct = true,
                //                    InsertDate = minDate,
                //                    LikeCount = 1
                //                },
                //                new()
                //                {
                //                    Name = "Product 1 - PAC2 - SKUCollection 2",
                //                    CollectionId = "P1_PAC2_SKU2",
                //                    IsFeaturedProduct = false,
                //                    InsertDate = maxDate,
                //                    LikeCount = 2
                //                }
                //            }
                //        }
                //    }
                //},
                //new()
                //{
                //    Id = 2,
                //    Name = "Product 2",
                //    InsertDate = maxDate,
                //    LikeCount = 2,
                //    SKUCollections = new()
                //    {
                //        new()
                //        {
                //            Name = "Product 2 - SKUCollection 1",
                //            CollectionId = "P2_SKU1",
                //            IsFeaturedProduct = true,
                //            InsertDate = minDate,
                //            LikeCount = 1
                //        },
                //        new()
                //        {
                //            Name = "Product 2 - SKUCollection 2",
                //            CollectionId = "P2_SKU2",
                //            IsFeaturedProduct = false,
                //            InsertDate = maxDate,
                //            LikeCount = 2
                //        }
                //    },
                //    ProductAttributeCombinations = new()
                //    {
                //        new()
                //        {
                //            Name = "Product 2 - PAC 1",
                //            SKUCollections = new()
                //            {
                //                new()
                //                {
                //                    Name = "Product 2 - PAC1 - SKUCollection 1",
                //                    CollectionId = "P2_PAC1_SKU1",
                //                    IsFeaturedProduct = true,
                //                    InsertDate = minDate,
                //                    LikeCount = 1
                //                },
                //                new()
                //                {
                //                    Name = "Product 2 - PAC1 - SKUCollection 2",
                //                    CollectionId = "P2_PAC1_SKU2",
                //                    IsFeaturedProduct = false,
                //                    InsertDate = maxDate,
                //                    LikeCount = 2
                //                }
                //            }
                //        },
                //        new()
                //        {
                //            Name = "Product 2 - PAC 2",
                //            SKUCollections = new()
                //            {
                //                new()
                //                {
                //                    Name = "Product 2 - PAC2 - SKUCollection 1",
                //                    CollectionId = "P2_PAC2_SKU1",
                //                    IsFeaturedProduct = true,
                //                    InsertDate = minDate,
                //                    LikeCount = 1
                //                },
                //                new()
                //                {
                //                    Name = "Product 2 - PAC2 - SKUCollection 2",
                //                    CollectionId = "P2_PAC2_SKU2",
                //                    IsFeaturedProduct = false,
                //                    InsertDate = maxDate,
                //                    LikeCount = 2
                //                }
                //            }
                //        }
                //    }
                //},
                //new()
                //{
                //    Id = 3,
                //    LikeCount = 5,
                //},
                //new()
                //{
                //    Id = 4,
                //    LikeCount = 5,
                //},
                //new()
                //{
                //    Id = 5,
                //    LikeCount = 6,
                //},
                //new()
                //{
                //    Id = 6,
                //    LikeCount = 6,
                //}
                #endregion
            };

            //var posts = new Post[]
            //{
            //    new Post{ Id = 7, Title = "صابون", Body = "", Tags = new List<Tag> { new Tag { Id = "a1", Name = "aa1" }, new Tag { Id = "a2", Name = "aa2" } } }, //fuzzy
            //    new Post{ Id = 8, Title = "بابون", Body = "", Tags = new List<Tag> { new Tag { Id = "b1", Name = "bb1" }, new Tag { Id = "b2", Name = "bb2" } } }, //fuzzy
            //    //new Post{ Id = 1, Title = "صابون لوسیون تست", Body = "" },
            //    //new Post{ Id = 2, Title = "تست صابون لوسیون", Body = "" },
            //    //new Post{ Id = 3, Title = "صابون لوسیون", Body = "" },
            //    //new Post{ Id = 4, Title = "صابون لوس تست", Body = "" }, //exact
            //    //new Post{ Id = 5, Title = "صابون لوس", Body = "" }, //exact
            //    //new Post{ Id = 6, Title = "لوسیون صابون", Body = "" }, //order
            //    //new Post{ Id = 9, Title = "صابون تست لوسین", Body = "" },
            //    //new Post{ Id = 7, Title = "", Body = "" },
            //    //new Post{ Id = 8, Title = "", Body = "" },
            //};

            //elasticClient.IndexMany(posts);
            elasticClient.Bulk(p => p
                .IndexMany(posts)
                .Refresh(Elasticsearch.Net.Refresh.True)
            );
        }

        private object Search(string query)
        {
            var result = elasticClient.Search<Product>(p => p
                .Query(p => p
                    .Match(p => p
                        .Field(p => p.Description)
                        .Query(query)
                    )
                )
            );

            var list = result.Hits.Select(p => new { p.Score, p.Source.Id, p.Source.Description }).ToList();
            return list;

            #region BoolQueryBuilder
            //BoolQueryBuilder<Product> boolQueryContainer = new BoolQueryBuilder<Product>();

            //boolQueryContainer.Filter(
            //    p => p.Term(p => p.Name, "Filter1"),
            //    p => p.Should(
            //        p => p.Term(p => p.Name, "Should1"),
            //        p => p.Term(p => p.Name, "Should2")
            //    )
            //).Must(
            //    p => p.Term(p => p.Name, "Must1"),
            //    p => p.Term(p => p.Name, "Must2")
            //);

            //var queryContainerDescriptor = new BoolQueryBuilder<Product>();

            //queryContainerDescriptor.Filter(
            //    p => p.Term(p => p.Name, "Filter1"),
            //    p => p.Should(
            //        p => p.Term(p => p.Name, "Should1"),
            //        p => p.Term(p => p.Name, "Should2")
            //    )
            //);
            //queryContainerDescriptor.Must(
            //    p => p.Term(p => p.Name, "Must1"),
            //    p => p.Term(p => p.Name, "Must2")
            //);
            #endregion

            #region SortDescriptor
            //var sort = new SortDescriptor<Product>();
            //sort.Descending(p => p.LikeCount);
            //sort.Ascending(p => p.Id);

            //var res = elasticClient.Search<Product>(p => p
            //    .Query(p => queryContainerDescriptor)
            //    .Sort(p => sort)
            //);
            #endregion

            #region Sort
            //Sort Desc by LikeCount Then by Desc Id
            //var resutl1 = elasticClient.Search<Product>(p => p
            //    .MatchAll()
            //    .Sort(p => p
            //        .Descending(p => p.LikeCount)
            //        .Descending(p => p.Id)
            //    )
            //);

            //Request:
            //POST http://192.168.53.224:9200/my-post-index/_search?pretty=true&error_trace=true&typed_keys=true
            //{
            //  "query": {
            //    "match_all": {}
            //  },
            //  "sort": [
            //    {
            //      "LikeCount": {
            //        "order": "desc"
            //      }
            //    },
            //    {
            //      "Id": {
            //        "order": "desc"
            //      }
            //    }
            //  ]
            //}
            #endregion

            #region (x && y) || (xx && yy)
            //var resutl1 = elasticClient.Search<Product>(p => p
            //    .Query(p => (
            //            +p.Term(p => p.Id, 5) &&
            //            +p.Term(p => p.Name, "p51")
            //        ) || (
            //            +p.Term(p => p.LikeCount, 111) &&
            //            +p.Term(p => p.LikeCount2, 12)
            //        )
            //    )
            //);

            //Request:
            //POST http://192.168.53.224:9200/my-post-index/_search?pretty=true&error_trace=true&typed_keys=true
            //{
            //  "query": {
            //    "bool": {
            //      "should": [
            //        {
            //          "bool": {
            //            "filter": [
            //              {
            //                "term": {
            //                  "Id": {
            //                    "value": 5
            //                  }
            //                }
            //              },
            //              {
            //                "term": {
            //                  "Name": {
            //                    "value": "p5"
            //                  }
            //                }
            //              }
            //            ]
            //          }
            //        },
            //        {
            //          "bool": {
            //            "filter": [
            //              {
            //                "term": {
            //                  "LikeCount": {
            //                    "value": 11
            //                  }
            //                }
            //              },
            //              {
            //                "term": {
            //                  "LikeCount2": {
            //                    "value": 12
            //                  }
            //                }
            //              }
            //            ]
            //          }
            //        }
            //      ]
            //    }
            //  }
            //}
            #endregion

            #region Null Checking (Exists)
            //var result1 = elasticClient.Search<Product>(p => p
            //        .Query(p =>
            //            !p.Exists(p => p.Field(p => p.InsertDate)) //&&
            //            //p.Term(p => p.LikeCount, null)
            //        )
            //    );

            //Request:
            //POST http://192.168.53.224:9200/my-post-index/_search?pretty=true&error_trace=true&typed_keys=true
            //{
            //  "query": {
            //    "bool": {
            //      "must_not": [
            //        {
            //          "exists": {
            //            "field": "InsertDate"
            //          }
            //        }
            //      ]
            //    }
            //  }
            //}
            #endregion

            #region Nested
            //var result1 = elasticClient.Search<Product>(p => p
            //    .Query(p =>
            //        //p.Term(p => p.SKUCollections.First().Name, "Product 1 - SKUCollection 1")
            //        p.Nested(p => p
            //            .Path(p => p.SKUCollections)
            //            .Query(p =>
            //                //p.Term(p => p.SKUCollections.First().Name, "Product 1 - SKUCollection 1") &&
            //                //p.Term(p => p.SKUCollections.First().IsFeaturedProduct, true)
            //                //OR
            //                p.Bool(p => p
            //                    .Must(
            //                        p => p.Term(p => p.SKUCollections.First().Name, "Product 1 - SKUCollection 1"),
            //                        p => p.Term(p => p.SKUCollections.First().IsFeaturedProduct, true)
            //                    )
            //                )
            //            )
            //        )
            //    )
            //);

            //Request:
            //POST http://192.168.53.224:9200/my-post-index/_search?pretty=true&error_trace=true&typed_keys=true
            //{
            //  "query": {
            //    "nested": {
            //      "path": "SKUCollections",
            //      "query": {
            //        "bool": {
            //          "must": [
            //            {
            //              "term": {
            //                "SKUCollections.Name": {
            //                  "value": "Product 1 - SKUCollection 1"
            //                }
            //              }
            //            },
            //            {
            //              "term": {
            //                "SKUCollections.IsFeaturedProduct": {
            //                  "value": true
            //                }
            //              }
            //            }
            //          ]
            //        }
            //      }
            //    }
            //  }
            //}
            #endregion

            #region Nested inside Nested
            //https://www.elastic.co/guide/en/elasticsearch/reference/current/query-dsl-nested-query.html#multi-level-nested-query-ex
            //Apparently both are the same so prefer to use one
            //Multi-level nesting is automatically supported, and detected, resulting in an inner nested query to automatically
            //match the relevant nesting level, rather than root, if it exists within another nested query.

            //var result2 = elasticClient.Search<Product>(p => p
            //    .Query(p =>
            //        p.Nested(p => p
            //            .Path(p => p.ProductAttributeCombinations.First().SKUCollections)
            //            .Query(p =>
            //                p.Bool(p => p
            //                    .Must(
            //                        p => p.Term(p => p.ProductAttributeCombinations.First().SKUCollections.First().Name, "Product 1 - PAC2 - SKUCollection 1"),
            //                        p => p.Term(p => p.ProductAttributeCombinations.First().SKUCollections.First().IsFeaturedProduct, true)
            //                    )
            //                )
            //            )
            //        )
            //    )
            //);

            //Request:
            //POST http://192.168.53.224:9200/my-post-index/_search?pretty=true&error_trace=true&typed_keys=true
            //{
            //  "query": {
            //    "nested": {
            //      "path": "ProductAttributeCombinations.SKUCollections",
            //      "query": {
            //        "bool": {
            //          "must": [
            //            {
            //              "term": {
            //                "ProductAttributeCombinations.SKUCollections.Name": {
            //                  "value": "Product 1 - PAC2 - SKUCollection 1"
            //                }
            //              }
            //            },
            //            {
            //              "term": {
            //                "ProductAttributeCombinations.SKUCollections.IsFeaturedProduct": {
            //                  "value": true
            //                }
            //              }
            //            }
            //          ]
            //        }
            //      }
            //    }
            //  }
            //}

            //var result3 = elasticClient.Search<Product>(p => p
            //    .Query(p =>
            //        p.Nested(p => p
            //            .Path(p => p.ProductAttributeCombinations)
            //            .Query(p =>
            //                p.Nested(p => p
            //                    .Path(p => p.ProductAttributeCombinations.First().SKUCollections)
            //                    .Query(p =>
            //                        p.Bool(p => p
            //                            .Must(
            //                                p => p.Term(p => p.ProductAttributeCombinations.First().SKUCollections.First().Name, "Product 1 - PAC2 - SKUCollection 1"),
            //                                p => p.Term(p => p.ProductAttributeCombinations.First().SKUCollections.First().IsFeaturedProduct, true)
            //                            )
            //                        )
            //                    )
            //                )
            //            )
            //        )
            //    )
            //);

            //Request:
            //POST http://192.168.53.224:9200/my-post-index/_search?pretty=true&error_trace=true&typed_keys=true
            //{
            //  "query": {
            //    "nested": {
            //      "path": "ProductAttributeCombinations",
            //      "query": {
            //        "nested": {
            //          "path": "ProductAttributeCombinations.SKUCollections",
            //          "query": {
            //            "bool": {
            //              "must": [
            //                {
            //                  "term": {
            //                    "ProductAttributeCombinations.SKUCollections.Name": {
            //                      "value": "Product 1 - PAC2 - SKUCollection 1"
            //                    }
            //                  }
            //                },
            //                {
            //                  "term": {
            //                    "ProductAttributeCombinations.SKUCollections.IsFeaturedProduct": {
            //                      "value": true
            //                    }
            //                  }
            //                }
            //              ]
            //            }
            //          }
            //        }
            //      }
            //    }
            //  }
            //}
            #endregion

            #region MyRegion
            //response = elasticClient.Search<Post>(p => p
            //    .Query(p => p
            //        .Match(p => p
            //            .Field(p => p.Title)
            //            .Query(query)
            //        .Fuzziness(Fuzziness.Auto)
            //        .FuzzyTranspositions()
            //        .Boost(1.5)
            //        ) ||
            //        p.Match(p => p
            //            .Field(p => p.Body)
            //            .Query(query)
            //        .Fuzziness(Fuzziness.Auto)
            //        .FuzzyTranspositions()
            //        .Boost(1)
            //        )
            //    )
            //);

            //response = elasticClient.Search<Post>(p => p
            //    .Query(p => p
            //        .MatchBoolPrefix(p => p
            //            .Field(p => p.Title)
            //            .Query(query)
            //            .Fuzziness(Fuzziness.Auto)
            //            .FuzzyTranspositions()
            //            .Boost(1.5)
            //        ) ||
            //        p.MatchBoolPrefix(p => p
            //            .Field(p => p.Body)
            //            .Query(query)
            //            .Fuzziness(Fuzziness.Auto)
            //            .FuzzyTranspositions()
            //            .Boost(1)
            //        )
            //    )
            //);

            //Test with actual data and user query for better result
            //TODO: ترکیب دکارتی با title/body boosts
            //response = elasticClient.Search<Post>(p => p
            //    .Query(p =>
            //        p.Match(p => p //without prefix without fuzzy
            //            .Field(p => p.Title)
            //            .Query(query)
            //            .Boost(1.4)
            //        ) ||
            //        p.MatchBoolPrefix(p => p //with prefix without fuzzy
            //            .Field(p => p.Title)
            //            .Query(query)
            //            .Boost(1.3)
            //        ) ||
            //        p.Match(p => p //without prefix with fuzzy
            //            .Field(p => p.Title)
            //            .Query(query)
            //            .Fuzziness(Fuzziness.Auto)
            //            .FuzzyTranspositions()
            //            .Boost(1.2)
            //        ) ||
            //        p.MatchBoolPrefix(p => p //with prefix with fuzzy
            //            .Field(p => p.Title)
            //            .Query(query)
            //            .Fuzziness(Fuzziness.Auto)
            //            .FuzzyTranspositions()
            //            .Boost(1.1)
            //        )
            //    )
            //);

            //return response.Hits.Select(p => new { p.Score, p.Source.Id, p.Source.Title, p.Source.Body }).ToList();
            #endregion

            return new[] { new { Score = 1, Id = 1, Title = "", Body = "" } };
        }

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            dataGridView1.DataSource = Search(textBox1.Text);
        }
    }
#pragma warning restore S2479 // Whitespace and control characters in string literals should be explicit
#pragma warning restore S125 // Sections of code should not be commented out
#pragma warning restore S1481 // Unused local variables should be removed

    public class Product
    {
        public int Id { get; set; }
        //[Keyword]
        //public string Name { get; set; }
        [Text]
        public string Description { get; set; }

        //public int? LikeCount { get; set; }
        //public DateTime? InsertDate { get; set; }
        //public List<string> str { get; set; }

        //[Nested]
        //public List<SKUCollection> SKUCollections { get; set; }
        //[Nested]
        //public List<ProductAttributeCombination> ProductAttributeCombinations { get; set; }
    }

    public class ProductAttributeCombination
    {
        [Keyword]
        public string Name { get; set; }
        [Nested]
        public List<SKUCollection> SKUCollections { get; set; }
    }

    public class SKUCollection
    {
        [Keyword]
        public string Name { get; set; }
        [Keyword]
        public string CollectionId { get; set; }
        public bool IsFeaturedProduct { get; set; }
        public int? LikeCount { get; set; }
        public DateTime? InsertDate { get; set; }
    }

    public enum ProductType
    {
        Type1,
        Type2,
        Type3
    }

    //public class Product
    //{
    //    public string Id { get; set; }
    //    public string BrandId { get; set; }
    //}

    public class BoolQueryBuilder<T> where T : class
    {
        private readonly QueryContainerDescriptor<T> queryContainerDescriptor;

        public BoolQueryBuilder(QueryContainerDescriptor<T> queryContainerDescriptor) : base()
        {
            this.queryContainerDescriptor = queryContainerDescriptor ?? throw new ArgumentNullException(nameof(queryContainerDescriptor));
        }

        public BoolQueryBuilder() : this(new())
        {
        }

        private string name;
        private double? boost;
        private MinimumShouldMatch minimumShouldMatches;
        private readonly List<Func<QueryContainerDescriptor<T>, QueryContainer>> must = new();
        private readonly List<Func<QueryContainerDescriptor<T>, QueryContainer>> mustNot = new();
        private readonly List<Func<QueryContainerDescriptor<T>, QueryContainer>> should = new();
        private readonly List<Func<QueryContainerDescriptor<T>, QueryContainer>> filter = new();

        public static implicit operator QueryContainer(BoolQueryBuilder<T> @this)
        {
            return @this.queryContainerDescriptor.Bool(p => p
                .Name(@this.name)
                .Boost(@this.boost)
                .MinimumShouldMatch(@this.minimumShouldMatches)
                .Must(@this.must)
                .MustNot(@this.mustNot)
                .Should(@this.should)
                .Filter(@this.filter)
            );
        }

        #region Methods

        public BoolQueryBuilder<T> MinimumShouldMatch(string name)
        {
            this.name = name;
            return this;
        }

        public BoolQueryBuilder<T> MinimumShouldMatch(double? boost)
        {
            this.boost = boost;
            return this;
        }

        public BoolQueryBuilder<T> MinimumShouldMatch(MinimumShouldMatch minimumShouldMatches)
        {
            this.minimumShouldMatches = minimumShouldMatches;
            return this;
        }

        public BoolQueryBuilder<T> Must(params Func<QueryContainerDescriptor<T>, QueryContainer>[] queries)
        {
            if (queries is null)
                throw new ArgumentNullException(nameof(queries));

            foreach (var query in queries)
                must.Add(query);

            return this;
        }

        public BoolQueryBuilder<T> Must(IEnumerable<Func<QueryContainerDescriptor<T>, QueryContainer>> queries)
        {
            if (queries is null)
                throw new ArgumentNullException(nameof(queries));

            foreach (var query in queries)
                must.Add(query);

            return this;
        }

        public BoolQueryBuilder<T> MustNot(params Func<QueryContainerDescriptor<T>, QueryContainer>[] queries)
        {
            if (queries is null)
                throw new ArgumentNullException(nameof(queries));

            foreach (var query in queries)
                mustNot.Add(query);

            return this;
        }

        public BoolQueryBuilder<T> MustNot(IEnumerable<Func<QueryContainerDescriptor<T>, QueryContainer>> queries)
        {
            if (queries is null)
                throw new ArgumentNullException(nameof(queries));

            foreach (var query in queries)
                mustNot.Add(query);

            return this;
        }

        public BoolQueryBuilder<T> Should(params Func<QueryContainerDescriptor<T>, QueryContainer>[] queries)
        {
            if (queries is null)
                throw new ArgumentNullException(nameof(queries));

            foreach (var query in queries)
                should.Add(query);

            return this;
        }

        public BoolQueryBuilder<T> Should(IEnumerable<Func<QueryContainerDescriptor<T>, QueryContainer>> queries)
        {
            if (queries is null)
                throw new ArgumentNullException(nameof(queries));

            foreach (var query in queries)
                should.Add(query);

            return this;
        }

        public BoolQueryBuilder<T> Filter(params Func<QueryContainerDescriptor<T>, QueryContainer>[] queries)
        {
            if (queries is null)
                throw new ArgumentNullException(nameof(queries));

            foreach (var query in queries)
                filter.Add(query);

            return this;
        }

        public BoolQueryBuilder<T> Filter(IEnumerable<Func<QueryContainerDescriptor<T>, QueryContainer>> queries)
        {
            if (queries is null)
                throw new ArgumentNullException(nameof(queries));

            foreach (var query in queries)
                filter.Add(query);

            return this;
        }

        #endregion
    }

    public static class ElasticSearchQueryExtensions
    {

        #region Bool

        public static BoolQueryBuilder<T> Must<T>(this QueryContainerDescriptor<T> queryContainer, params Func<QueryContainerDescriptor<T>, QueryContainer>[] queries)
            where T : class
        {
            var boolQueryBuilder = new BoolQueryBuilder<T>(queryContainer);
            boolQueryBuilder.Must(queries);
            return boolQueryBuilder;
        }

        public static BoolQueryBuilder<T> Must<T>(this QueryContainerDescriptor<T> queryContainer, IEnumerable<Func<QueryContainerDescriptor<T>, QueryContainer>> queries)
            where T : class
        {
            var boolQueryBuilder = new BoolQueryBuilder<T>(queryContainer);
            boolQueryBuilder.Must(queries);
            return boolQueryBuilder;
        }

        public static BoolQueryBuilder<T> MustNot<T>(this QueryContainerDescriptor<T> queryContainer, params Func<QueryContainerDescriptor<T>, QueryContainer>[] queries)
            where T : class
        {
            var boolQueryBuilder = new BoolQueryBuilder<T>(queryContainer);
            boolQueryBuilder.MustNot(queries);
            return boolQueryBuilder;
        }

        public static BoolQueryBuilder<T> MustNot<T>(this QueryContainerDescriptor<T> queryContainer, IEnumerable<Func<QueryContainerDescriptor<T>, QueryContainer>> queries)
            where T : class
        {
            var boolQueryBuilder = new BoolQueryBuilder<T>(queryContainer);
            boolQueryBuilder.MustNot(queries);
            return boolQueryBuilder;
        }

        public static BoolQueryBuilder<T> Should<T>(this QueryContainerDescriptor<T> queryContainer, params Func<QueryContainerDescriptor<T>, QueryContainer>[] queries)
            where T : class
        {
            var boolQueryBuilder = new BoolQueryBuilder<T>(queryContainer);
            boolQueryBuilder.Should(queries);
            return boolQueryBuilder;
        }

        public static BoolQueryBuilder<T> Should<T>(this QueryContainerDescriptor<T> queryContainer, IEnumerable<Func<QueryContainerDescriptor<T>, QueryContainer>> queries)
            where T : class
        {
            var boolQueryBuilder = new BoolQueryBuilder<T>(queryContainer);
            boolQueryBuilder.Should(queries);
            return boolQueryBuilder;
        }

        public static BoolQueryBuilder<T> Filter<T>(this QueryContainerDescriptor<T> queryContainer, params Func<QueryContainerDescriptor<T>, QueryContainer>[] queries)
            where T : class
        {
            var boolQueryBuilder = new BoolQueryBuilder<T>(queryContainer);
            boolQueryBuilder.Filter(queries);
            return boolQueryBuilder;
        }

        public static BoolQueryBuilder<T> Filter<T>(this QueryContainerDescriptor<T> queryContainer, IEnumerable<Func<QueryContainerDescriptor<T>, QueryContainer>> queries)
            where T : class
        {
            var boolQueryBuilder = new BoolQueryBuilder<T>(queryContainer);
            boolQueryBuilder.Filter(queries);
            return boolQueryBuilder;
        }

        #endregion
    }
}