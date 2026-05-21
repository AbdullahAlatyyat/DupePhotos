namespace DupePhotos.Core;

public static class DupePhotosCoreServiceFactory
{
    public static IDuplicateDetector CreateDuplicateDetector()
    {
        return new DuplicateDetector(new ImageScanner(), new ImageHasher());
    }
}
