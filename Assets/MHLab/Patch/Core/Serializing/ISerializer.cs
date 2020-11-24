namespace MHLab.Patch.Core.Serializing
{
    public interface ISerializer
    {
        string Serialize<TObject>(TObject obj);
        TObject Deserialize<TObject>(string data);
    }
}
