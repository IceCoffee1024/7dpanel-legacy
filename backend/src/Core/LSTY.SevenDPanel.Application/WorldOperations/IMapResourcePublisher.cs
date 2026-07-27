using System;

namespace LSTY.SevenDPanel.Application.WorldOperations
{
    public sealed record MapResourcePublication(
        string WorldId,
        string MapResourceVersion,
        int TileSize);

    public interface IMapResourcePublisher
    {
        MapResourcePublication Publish(string expectedWorldId, string stagedRoot);
    }

    public class MapResourcePublishException : Exception
    {
        public MapResourcePublishException(string errorCode)
            : base(errorCode) => ErrorCode = errorCode;

        public MapResourcePublishException(string errorCode, Exception innerException)
            : base(errorCode, innerException) => ErrorCode = errorCode;

        public string ErrorCode { get; }
    }
}
