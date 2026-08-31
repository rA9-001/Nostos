using Xunit;

// One test class at a time in this assembly.
//
// The interface language is a static: there is one window, one person reading it, and one
// answer to "what language is this in". That is right for the program and wrong for a test
// runner that runs classes in parallel by default -- a test that switches to German for three
// lines was switching it underneath every other class at the same time, and the failures landed
// in whichever test happened to be reading English text at that moment.
//
// Disabling parallelism rather than isolating the language, because these suites run in well
// under a second and a lock or a fixture would be machinery protecting an assumption that is
// only untrue inside a test runner.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
