using Xunit;

// Several classes under test keep a process-wide "current" instance (FormatIndex.Current, InfoService.Current, PlaybackActivity.Current, ...) that the
// next constructed instance replaces. Test classes that run side by side on separate threads overwrote each other's (an intermittent failure of
// HiResTests on the merged tree). The whole suite takes seconds, so the tests run one after another.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
