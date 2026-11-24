// UI tests share the screen, the mouse and the app's single instance, so they must never run at the same time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
