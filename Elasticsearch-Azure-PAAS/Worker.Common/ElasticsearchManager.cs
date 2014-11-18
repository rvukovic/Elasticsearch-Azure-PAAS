﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using YamlDotNet.RepresentationModel;

namespace Worker.Common
{
    public class ElasticsearchManager : SoftwareManager
    {
        public const string ELASTICSEARCH_CONFIG_FILE = "elasticsearch.yaml";
        public const string ELASTICSEARCH_LOG_CONFIG_FILE = "logging.yaml";

        protected string _elasticRoot;
        protected string _pluginRoot;
        protected ElasticsearchRuntimeConfig _config;
        protected Process _process = null;
        public ElasticsearchManager(ElasticsearchRuntimeConfig _config, WebArtifact artifact, string archiveRoot, string elasticRoot, string logRoot)
            : base(artifact, archiveRoot, logRoot)
        {
            _elasticRoot = elasticRoot;
            _pluginRoot = Path.Combine(_elasticRoot, "plugins");
        }

        public override Task EnsureConfigured()
        {
            return Task.Factory.StartNew(() =>
            {
                //if elasticsearch zip does not exist download it
                DownloadIfNotExists();

                //clear installation root and extract archive to installation root
                //Should not be necessary with non-persisted resource directory
                Install();

                //Extract all packaged plugins to plugin folder 
                ConfigurePlugins();

                //Write elasticsearch.yaml
                ConfigureElasticsearch();

                //Write logging.yaml
                ConfigureElastisearchLogging();

            });




        }

        protected virtual void Install()
        {
            //Always trash and recreate elasticsearch folder.
            //The resource directory should not be persisted through recycles
            Trace.TraceInformation("Re-creating elasticshearch root");
            var dir = new DirectoryInfo(_elasticRoot);

            foreach (var file in dir.EnumerateFiles())
            {
                file.Delete();
            }

            foreach (var folder in dir.EnumerateDirectories())
            {
                folder.Delete();
            }

            Trace.TraceInformation("Extracting elasticsearch");
            ZipFile.ExtractToDirectory(_binaryArchive, _elasticRoot);
        }

        protected virtual void ConfigureElasticsearch()
        {
            string configRoot = Path.Combine(_elasticRoot, "Config");
            if (!Directory.Exists(configRoot))
            {
                Directory.CreateDirectory(configRoot);
            }
            string configFile = Path.Combine(configRoot,ELASTICSEARCH_CONFIG_FILE);

            using (var input = new StreamReader(_config.TemplateConfigFile))
            using (var output = new StreamWriter(configFile, false))
            {
                Trace.WriteLine("Loading Default Config");
                var config = new Dictionary<string, string>();
                // Load the stream
                var yamlInput = new YamlStream();
                yamlInput.Load(input);


                var rootOutputNode = new YamlMappingNode();
                var outputDoc = new YamlDocument(rootOutputNode);
                var yamlOutput = new YamlStream(outputDoc);


                if (yamlInput.Documents.Count > 0)
                {
                    var mapping = (YamlMappingNode)yamlInput.Documents[0].RootNode;
                    var reservedConfigs = new string[] { "path.data", "path.work", "path.logs", "path.plugins" };
                    foreach (var entry in mapping.Children.Where(m => !reservedConfigs.Contains(m.Key.ToString())))
                    {
                        rootOutputNode.Add(entry.Key, entry.Value);
                    }
                }

                Trace.WriteLine("Writing Critical Config values");
                //write important config values reglardless of what was provided in package

                rootOutputNode.Add(new YamlScalarNode("path.data"), new YamlScalarNode(_config.DataPath));
                rootOutputNode.Add(new YamlScalarNode("path.work"), new YamlScalarNode(_config.TempPath));
                rootOutputNode.Add(new YamlScalarNode("path.logs"), new YamlScalarNode(_config.LogPath));
                rootOutputNode.Add(new YamlScalarNode("path.plugin"), new YamlScalarNode(_pluginRoot));
                rootOutputNode.Add(new YamlScalarNode("node.name"), new YamlScalarNode(_config.NodeName));
                rootOutputNode.Add(new YamlScalarNode("cloud.azureruntime.bridge"), new YamlScalarNode(_config.BridgePipeName));

                Trace.WriteLine("Saving Config File");
                yamlOutput.Save(output);

            }

        }

        protected virtual void ConfigurePlugins()
        {
            var packagePlugins = Directory.GetFiles(_config.PackagePluginPath, "*.zip");
           
            Directory.CreateDirectory(_pluginRoot);

            foreach (var file in packagePlugins)
            {
                var pluginFileName = Path.GetFileNameWithoutExtension(file);
	            var pluginPath = Path.Combine(_pluginRoot,pluginFileName);

                ZipFile.ExtractToDirectory(file, pluginPath);
            }
        }

        protected virtual void ConfigureElastisearchLogging()
        {
            string configFile = Path.Combine(_elasticRoot, "Config", ELASTICSEARCH_LOG_CONFIG_FILE);
            File.Copy(_config.TemplateLogConfigFile, configFile);
        }

        public virtual void StartAndBlock()
        {
            _process = new Process();
            _process.StartInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(_elasticRoot, "bin", "elasticsearch.bat")
            };
            _process.Start();
            _process.WaitForExit();
        }

        public virtual void Stop()
        {
            if (_process!= null && !_process.HasExited)
            {
                _process.CloseMainWindow();
            }
        }
    }
}