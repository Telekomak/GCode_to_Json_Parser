using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GCodeManager
{
    static class GParser
    { 
        public static GCode Parse(string[] Gcode)
        {
            if (Gcode == null)
            {
                throw new ArgumentNullException();
            }

            JArray codeParsed = new JArray();

            string[] codeClean = Gcode
                .Where(x => x.Length > 1 && ((x[0] == 'G' || x[0] == 'M') && Int32.TryParse(x[1].ToString(), out int m))).ToArray();

            List<Task<JArray>> parsingTasks = new List<Task<JArray>>();

            string[][] codeFragments = ScheduleArray(codeClean.ToList(), 8);

            foreach (string[] parsingTask in codeFragments)
            {
                parsingTasks.Add(new Task<JArray>(() => ParseFragment(parsingTask)));
            }

            var result = Task.Run(async () => await Task.WhenAll(parsingTasks));
            codeParsed.Merge(result.Result);

            CalculateLayersReturnData calData = CalculateLayers(codeParsed, Gcode[0]);
            return new GCode(String.Concat(GenerateGCodeAsync(calData.Layers).Result), Gcode[0], calData.Json, calData.LayerCount, calData.Layers.ToArray());
        }

        public static GCode Parse(string filename)
        {
            var result = Task.Run(async () => await LoadFileAsync(filename));
            string[] Gcode = result.Result;

            if (Gcode == null)
            {
                throw new ArgumentNullException();
            }

            JArray codeParsed = new JArray();

            string[] codeClean = Gcode
                .Where(x => x.Length > 1 && ((x[0] == 'G' || x[0] == 'M') && Int32.TryParse(x[1].ToString(), out int m))).ToArray();

            List<Task<JArray>> parsingTasks = new List<Task<JArray>>();

            string[][] codeFragments = ScheduleArray(codeClean.ToList(), 8);

            foreach (string[] parsingTask in codeFragments)
            {
                parsingTasks.Add(new Task<JArray>(() => ParseFragment(parsingTask)));
            }

            var result1 = Task.Run(async () => await Task.WhenAll(parsingTasks));
            codeParsed.Merge(result1.Result);

            CalculateLayersReturnData calData = CalculateLayers(codeParsed, Gcode[0]);
            return new GCode(String.Concat(GenerateGCodeAsync(calData.Layers).Result), Gcode[0], calData.Json, calData.LayerCount, calData.Layers.ToArray());
        }

        public static async Task<GCode> ParseAsync(string[] Gcode)
        {
            if (Gcode == null)
            {
                throw new ArgumentNullException();
            }

            JArray codeParsed = new JArray();

            string[] codeClean = Gcode
                .Where(x => x.Length > 1 && ((x[0] == 'G' || x[0] == 'M') && Int32.TryParse(x[1].ToString(), out int m))).ToArray();

            List<Task<JArray>> parsingTasks = new List<Task<JArray>>();

            string[][] codeFragments = ScheduleArray(codeClean.ToList(), 8);

            foreach (string[] parsingTask in codeFragments)
            {
                parsingTasks.Add(new Task<JArray>(() => ParseFragment(parsingTask)));
            }

            codeParsed.Merge(await Task.WhenAll(parsingTasks));

            CalculateLayersReturnData calData = CalculateLayers(codeParsed, Gcode[0]);
            return new GCode(String.Concat(await GenerateGCodeAsync(calData.Layers)), Gcode[0], calData.Json, calData.LayerCount, calData.Layers.ToArray());
        }

        public static async Task<GCode> ParseAsync(string filename)
        {
            string[] Gcode = await LoadFileAsync(filename);

            if (Gcode == null)
            {
                throw new ArgumentNullException();
            }
            
            JArray codeParsed = new JArray();

            string[] codeClean = Gcode
                .Where(x => x.Length > 1 && ((x[0] == 'G' || x[0] == 'M') && Int32.TryParse(x[1].ToString(), out int m))).ToArray();

            List<Task<JArray>> parsingTasks = new List<Task<JArray>>();

            string[][] codeFragments = ScheduleArray(codeClean.ToList(), 8);

            foreach (string[] parsingTask in codeFragments)
            {
                var t = new Task<JArray>(() => ParseFragment(parsingTask));
                t.Start();
                parsingTasks.Add(t);
            }

            codeParsed.Merge(await Task.WhenAll(parsingTasks.ToArray()));
            var layerTask = new Task<CalculateLayersReturnData>(() => CalculateLayers(codeParsed, Gcode[0]));
            layerTask.Start();
            CalculateLayersReturnData calData = await layerTask;
            return new GCode(String.Concat(await GenerateGCodeAsync(calData.Layers)), Gcode[0], calData.Json, calData.LayerCount, calData.Layers.ToArray());
        }
        private static JArray ParseFragment(string[] codeFragment)
        {
            JArray codeParsed = new JArray();

            foreach (string line in codeFragment)
            {
                string[] function = Encoding.ASCII.GetString(Encoding.ASCII.GetBytes(line.ToCharArray())).Split(' ').Where(x =>
                    x != "" && (((byte)x[0] >= 65 && (byte)x[0] <= 90) && Int32.TryParse(x[x.Length-1].ToString(), out int m))).ToArray();//TODO Regex

                JObject functionProps = new JObject();

                for (int i = 1; i < function.Length; i++)
                {
                    functionProps.Add(new JProperty(function[i][0].ToString(), function[i].Remove(0, 1)));
                }

                codeParsed.Add(new JObject(new JProperty(function[0], functionProps)));
            }

            return codeParsed;
        }
        private static string[][] ScheduleArray(List<string> collection, int taskCount)
        {
            int fragmentSize = collection.Count / (taskCount - 1);
            string[][] fragments = new string[taskCount][];

            for (int i = 0; i < taskCount-1; i++)
            {
                fragments[i] = collection.GetRange(i * fragmentSize, fragmentSize).ToArray();
            }

            if (collection.Count % (taskCount - 1) != 0)
            {
                fragments[taskCount-1] = collection.GetRange((taskCount-1)*fragmentSize, collection.Count % (taskCount - 1)).ToArray();
            }
            
            return fragments;
        }
        private static CalculateLayersReturnData CalculateLayers(JArray parsedCode, string name)
        {
            decimal zValue = 0;
            decimal initLayerHeight = -1;
            int layerCount = 0;

            #region Old code
            //JObject codeOutput = new JObject();
            //JObject currentLayer;

            //codeOutput.Add(new JProperty($"LAYER_{layerCount}", new JObject(
            //        new JProperty("LayerNumber", layerCount),
            //        new JProperty("LayerHeight",0), 
            //        new JProperty("LayerCode", new JArray()))));

            //currentLayer = (JObject)codeOutput.Property($"LAYER_{ layerCount}").Value;

            //foreach (JArray item in parsedCode)
            //{
            //    foreach (JObject function in item)
            //    {
            //        if (function.ContainsKey("G0"))
            //        {
            //            JObject functionProps = (JObject)function.Property("G0").Value;

            //            if (functionProps.ContainsKey("Z"))
            //            {
            //                if (initLayerHeight == -1) initLayerHeight = (double)functionProps.Property("Z").Value;

            //                if ((double)functionProps.Property("Z").Value >= zValue)
            //                {
            //                    layerCount++;
            //                    codeOutput.Add(new JProperty($"LAYER_{layerCount}", new JObject(
            //                        new JProperty("LayerNumber", layerCount),
            //                        new JProperty("LayerHeight", (double)functionProps.Property("Z").Value - zValue),
            //                        new JProperty("LayerCode", new JArray()))));
            //                }
            //                else
            //                {
            //                    codeOutput.Property($"LAYER_{ layerCount - 1}").Add(currentLayer.Property("LayerCode").Value);
            //                    codeOutput.Remove($"LAYER_{layerCount}");
            //                    layerCount--;
            //                }

            //                currentLayer = (JObject)codeOutput.Property($"LAYER_{ layerCount}").Value;
            //                zValue = (double)functionProps.Property("Z").Value;
            //            }
            //        }

            //        JArray k = (JArray)currentLayer.Property("LayerCode").Value;
            //        k.Add(function);
            //    }
            //}
            //Comparer to check:https://countwordsfree.com/comparetexts
            #endregion

            List<Layer> layers = new List<Layer>();
            List<string> stringFormat = new List<string>();

            layers.Add(new Layer(layerCount, 0, 0));
            stringFormat.Add($"Layer: {layerCount} \n");

            foreach (JArray item in parsedCode)
            {
                foreach (JObject function in item)
                {
                    if (function.ContainsKey("G0"))
                    {
                        JObject functionProps = (JObject)function.Property("G0").Value;

                        if (functionProps.ContainsKey("Z"))
                        {
                            if (initLayerHeight == -1) initLayerHeight = (decimal)functionProps.Property("Z").Value;

                            if ((decimal)functionProps.Property("Z").Value >= zValue)
                            {
                                layerCount++;
                                layers.Add(new Layer(layerCount, (decimal)functionProps.Property("Z").Value - zValue, (decimal)functionProps.Property("Z").Value));
                                stringFormat.Add($"Layer: {layerCount} \n");
                            }
                            else
                            {
                                layers[layerCount-1].LayerCode.AddRange(layers[layerCount].LayerCode);
                                stringFormat[layerCount - 1] += stringFormat[layerCount];
                                layers.RemoveAt(layerCount);
                                stringFormat.RemoveAt(layerCount);
                                layerCount--;
                            }

                            zValue = (decimal)functionProps.Property("Z").Value;
                        }
                    }

                    layers[layerCount].LayerCode.Add(function);
                }
            }

            return new CalculateLayersReturnData(
                new JObject(new JProperty(name, new JObject(
                new JProperty("LAYER_COUNT", layerCount),
                new JProperty("INITIAL_LAYER_HEIGHT", (float)initLayerHeight),
                new JProperty("GCODE", new JArray(JArray.FromObject(layers)))))), 
                layers, layerCount);
        }
        private static string GetFuncString(JObject function)
        {
            string funcString = "   ";
            funcString += function.Properties().ToArray()[0].Name + " (";
            function = (JObject)function.Properties().ToArray()[0].Value;
            
            for (int i = 1; i < function.Values().Count(); i++)
            {
                funcString += $"{function.Properties().ToArray()[i].Name}: {function.Properties().ToArray()[i].Value}, ";
            }

            funcString += ") \n";
            return funcString;
        }
        public static async Task<string[]> GenerateGCodeAsync(GCode gCode)
        {
            List<Task<string>> parsingTasks = new List<Task<string>>();

            foreach (Layer[] layers in ScheduleArray(gCode.Layers.ToList(), 8))
            {
                var t = new Task<string>(() => ParseFragment(layers));
                t.Start();
                parsingTasks.Add(t);
            }

            return (await Task.WhenAll(parsingTasks)).ToArray();
        }
        public static async Task<string[]> GenerateGCodeAsync(List<Layer> code)
        {
            List<Task<string>> parsingTasks = new List<Task<string>>();

            foreach (Layer[] layers in ScheduleArray(code.ToList(), 8))
            {
                var t = new Task<string>(() => ParseFragment(layers));
                t.Start();
                parsingTasks.Add(t);
            }

            return (await Task.WhenAll(parsingTasks.ToArray()));
        }
        private static string ParseFragment(Layer[] layers)
        {
            string retData = "";
            string func;
            JObject prop;
            
            foreach (Layer layer in layers)
            {
                retData += $";LAYER: {layer.LayerNumber}\n";

                foreach (JObject item in layer.LayerCode)
                {
                    func = item.Properties().First().Name;
                    prop = (JObject)item.Properties().First().Value;
                    
                    foreach (JProperty property in prop.Properties())
                    {
                        func += $" {property.Name}{property.Value}";
                    }

                    func += "\n";
                    retData += func;
                }
            }

            return retData;
        }
        public static async Task<GCode> ParseLayer(int[] layerIndexes, GCode gCode)
        {
            
            SplittingOptions splittingOptions = GetSplittingOptions(layerIndexes, gCode);
            string[] temp = gCode.RawGCode.Split(splittingOptions.SplitArguments, StringSplitOptions.None);
            Layer[][] layersToParse = splittingOptions.LayersToParse;
            int index = -1;

            List<Task<string>> parsingTasks = new List<Task<string>>();

            foreach (Layer[] layers in layersToParse)
            {
                var t = new Task<string>(() => ParseFragment(layers));
                t.Start();
                parsingTasks.Add(t);
            }

            string[] parsedLayers = await Task.WhenAll(parsingTasks);

            for (int i = 0; i < parsedLayers.Length; i++)
            {
                temp[index+=2] = parsedLayers[i];
            }

            gCode.RawGCode = String.Concat(temp);
            return gCode;
        }
        private static SplittingOptions GetSplittingOptions(int[] indexes ,GCode gCode)
        {
            List<Layer[]> fragments = new List<Layer[]>();//{1},{3,4}
            List<Layer> fragment = new List<Layer>();//3,4

            List<string> splitList = new List<string>();//";LAYER: 1", ";LAYER: 2", ";LAYER: 3", ";LAYER: 5"

            splitList.Add($";LAYER: {indexes[0]}\n");
            fragment.Add(gCode.Layers[indexes[0]]);

            for (int i = 0; i < indexes.Length; i++)
            {
                if (i != 0)
                {
                    if (indexes[i] - indexes[i - 1] == 1)
                    {
                        fragment.Add(gCode.Layers[indexes[i]]);
                        continue;
                    }
                    fragments.Add(fragment.ToArray());
                    fragment = new List<Layer>();
                    fragment.Add(gCode.Layers[indexes[i]]);
                    splitList.Add($";LAYER: {indexes[i-1]+1}\n");
                    splitList.Add($";LAYER: {indexes[i]}\n");
                }
            }
            splitList.Add($";LAYER: {indexes[indexes.Length-1]+1}\n");
            fragments.Add(fragment.ToArray());

            return new SplittingOptions(splitList.ToArray(), fragments.ToArray());
        }
        private static Layer[][] ScheduleArray(List<Layer> collection, int taskCount)
        {
            int fragmentSize = collection.Count / (taskCount - 1);
            Layer[][] fragments = new Layer[taskCount][];

            for (int i = 0; i < taskCount - 1; i++)
            {
                fragments[i] = collection.GetRange(i * fragmentSize, fragmentSize).ToArray();
            }

            if (collection.Count % (taskCount - 1) != 0)
            {
                fragments[taskCount - 1] = collection.GetRange((taskCount - 1) * fragmentSize, collection.Count % (taskCount - 1)).ToArray();
            }

            return fragments;
        }
        private static async Task<string[]> LoadFileAsync(string filename)
        {
            string text = filename + "\n";

            try
            {
                using (StreamReader reader = new StreamReader(new FileStream(filename, FileMode.Open)))
                {
                    text += reader.ReadToEnd();
                }
                return Regex.Split(text, "\r\n|\r|\n");
            }
            catch (Exception e)
            {
                MessageBox.Show("Error", "Error occured reading file: \n" + filename, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        } 

        private struct SplittingOptions
        {
            public string[] SplitArguments { get; private set; }
            public Layer[][] LayersToParse { get; private set; }

            public SplittingOptions(string[] splitArguments, Layer[][] layersToParse)
            {
                SplitArguments = splitArguments;
                LayersToParse = layersToParse;
            }
        }
        private struct CalculateLayersReturnData
        {
            public JObject Json;
            public List<Layer> Layers;
            public int LayerCount;

            public CalculateLayersReturnData(JObject json, List<Layer> layers, int layerCount)
            {
                Json = json;
                Layers = layers;
                LayerCount = layerCount;
            }
        }
    }
}
