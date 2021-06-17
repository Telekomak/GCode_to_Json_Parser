using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace GCodeParser
{
    [Serializable]
    public class GCode
    {
        public string RawGCode { get; set; }
        public string FileName { get; private set; }
        public JObject GCodeJson { get; set; }
        public int LayerCount { get; private set; }
        public Layer[] Layers { get; private set; }
        public string FilePath { get; private set; }

        public GCode(string rawGCode, string fileName, JObject gCodeJson, int layerCount, Layer[] layers)
        {
            RawGCode = rawGCode;
            FileName = fileName;
            GCodeJson = gCodeJson;
            LayerCount = layerCount;
            Layers = layers;
        }

        public GCode()
        {
            
        }

        public async Task SaveAsync(string filename)
        {
            using (FileStream stream = new FileStream(filename, FileMode.OpenOrCreate))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, this);
            }
        }

        public static async Task<GCode> LoadAsync(string filename)
        {
            using (FileStream stream = new FileStream(filename, FileMode.OpenOrCreate))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                return (GCode)formatter.Deserialize(stream);
            }
        }

        public void Save(string filename)
        {
            using (FileStream stream = new FileStream(filename, FileMode.OpenOrCreate))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                formatter.Serialize(stream, this);
            }
        }

        public static GCode Load(string filename)
        {
            using (FileStream stream = new FileStream(filename, FileMode.OpenOrCreate))
            {
                BinaryFormatter formatter = new BinaryFormatter();
                return (GCode)formatter.Deserialize(stream);
            }
        }
    }

    [Serializable]
    public class Layer
    {
        public int LayerNumber;
        public decimal LayerHeight;
        public decimal LayerZ;
        public List<JObject> LayerCode;

        public Layer(int layerNumber, decimal layerHeight, decimal layerZ)
        {
            LayerNumber = layerNumber;
            LayerHeight = layerHeight;
            LayerCode = new List<JObject>();
            LayerZ = layerZ;
        }

        public void InsertCode(string code, int line)
        {
            JObject functionProps = new JObject();
            string[] temp = code.Split('\n').Reverse().ToArray();
            string[] func;

            foreach (var item in temp)
            {
                func = item.Split(' ');

                for (int j = 1; j < func.Length; j++)
                {
                    functionProps.Add(new JProperty(func[j][0].ToString(), func[j].Remove(0, 1)));
                }

                LayerCode.Insert(line, new JObject(new JProperty(func[0], functionProps)));
                functionProps = new JObject();
            }
        }

        public override string ToString()
        {
            return $"Layer: {LayerNumber}, Z: {LayerZ}";
        }
    }
}
