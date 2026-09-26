using jaytwo.Ergonomics.Logging;
using Microsoft.Extensions.Logging;

namespace jaytwo.Ergonomics.S3.Logging;

internal static class EventIds
{
    private const string Prefix = "S3:";

    public static class ObjectEvents
    {
        public static readonly EventId ObjectPut = EventLogger.EventIdFromName(Prefix + "OBJECT_PUT");

        public static readonly EventId ObjectMultipartPut = EventLogger.EventIdFromName(Prefix + "OBJECT_MULTIPART_PUT");

        public static readonly EventId ObjectGet = EventLogger.EventIdFromName(Prefix + "OBJECT_GET");

        public static readonly EventId ObjectStreamClosed = EventLogger.EventIdFromName(Prefix + "OBJECT_STREAM_CLOSED");

        public static readonly EventId ObjectHead = EventLogger.EventIdFromName(Prefix + "OBJECT_HEAD");

        public static readonly EventId ObjectDeleted = EventLogger.EventIdFromName(Prefix + "OBJECT_DELETED");

        public static readonly EventId ObjectCopied = EventLogger.EventIdFromName(Prefix + "OBJECT_COPIED");

        public static readonly EventId ObjectListed = EventLogger.EventIdFromName(Prefix + "OBJECT_LISTED");

        public static readonly EventId ObjectExists = EventLogger.EventIdFromName(Prefix + "OBJECT_EXISTS");

        public static readonly EventId ObjectNotFound = EventLogger.EventIdFromName(Prefix + "OBJECT_NOT_FOUND");

        public static readonly EventId BucketExists = EventLogger.EventIdFromName(Prefix + "BUCKET_EXISTS");

        public static readonly EventId OperationFailed = EventLogger.EventIdFromName(Prefix + "OPERATION_FAILED");

        public static readonly EventId Cancelled = EventLogger.EventIdFromName(Prefix + "CANCELLED");
    }
}
