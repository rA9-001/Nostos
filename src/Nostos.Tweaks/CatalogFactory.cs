using Nostos.Core.Abstractions;
using Nostos.Core.Engine;
using Nostos.Tweaks.Declarative;
using Nostos.Tweaks.Native;
using Nostos.Win32.Services;

namespace Nostos.Tweaks;

/// <summary>Assembles the full catalog: declarative entries plus the ones that need real code.</summary>
public static class CatalogFactory
{
    public static IReadOnlyList<ITweak> CreateAll()
    {
        var tweaks = new List<ITweak>();

        foreach (var definition in RegistryTweakCatalog.LoadEmbedded())
            tweaks.Add(new RegistryTweak(definition));

        tweaks.Add(new UltimatePerformanceTweak());
        tweaks.Add(new GameProcessTuningTweak());
        tweaks.Add(new TcpLatencyTweak());
        tweaks.AddRange(NetworkAdapterTweaks());
        tweaks.AddRange(ServiceTweaks());

        return tweaks;
    }

    /// <summary>
    /// The network adapter settings that actually cost latency.
    ///
    /// This is the honest half of what a tool like SG TCP Optimizer offers. Most of what such
    /// tools set -- MTU, TTL, MaxUserPort, TcpTimedWaitDelay, selective ACKs, the congestion
    /// provider, receive-window auto-tuning -- is about throughput, connection setup or port
    /// exhaustion. Those are real settings with real effects, and none of them shortens the
    /// round trip of a packet that is already flowing, which is what a player means by ping.
    ///
    /// What does are these four: the NIC deliberately delaying an interrupt, the NIC
    /// deliberately merging packets, the link deliberately going to sleep, and the link
    /// deliberately telling the switch to stop sending. Each one trades latency for CPU or
    /// power on purpose, by design, and each one is documented as something to turn off for
    /// latency-sensitive traffic.
    ///
    /// All four live in the registry, per adapter, and survive a reboot -- which is the part
    /// that matters. A `netsh int tcp set global` setting is applied to a running stack and
    /// cannot be read back out of the registry, so this program could not prove afterwards what
    /// it had done. See docs/tweaks/network.receive-coalescing-off.md for the one case where
    /// that gap is worth knowing about.
    /// </summary>
    private static IEnumerable<ITweak> NetworkAdapterTweaks()
    {
        yield return new NetworkAdapterTweak(
            id: "network.interrupt-moderation-off",
            title: "Stop the network card batching its interrupts",
            summary: "The NIC holds arriving packets back for a fraction of a millisecond so it "
                   + "can raise one interrupt instead of several. That delay is the whole point "
                   + "of the feature, and it lands on every packet a game receives.",
            keywords: ["*InterruptModeration"],
            target: "0",
            absentReason: "no network adapter on this machine exposes an interrupt moderation "
                        + "setting, so there is nothing to turn off",
            tags: ["network", "adapter", "latency", "interrupts"]);

        yield return new NetworkAdapterTweak(
            id: "network.receive-coalescing-off",
            title: "Stop the network card merging received packets",
            summary: "Receive Segment Coalescing waits for several TCP segments and hands them "
                   + "up as one. It saves CPU by adding delay, and Microsoft's own guidance is "
                   + "to turn it off for latency-sensitive traffic.",
            keywords: ["*RscIPv4", "*RscIPv6", "*WdiRscIPv4", "*WdiRscIPv6", "*PacketCoalescing"],
            target: "0",
            absentReason: "no network adapter on this machine exposes a per-adapter coalescing "
                        + "setting; on this machine it can only be changed globally, with "
                        + "`netsh int tcp set global rsc=disabled`",
            tags: ["network", "adapter", "latency", "rsc", "coalescing"]);

        yield return new NetworkAdapterTweak(
            id: "network.energy-efficient-ethernet-off",
            title: "Stop the network link powering down between packets",
            summary: "Energy Efficient Ethernet parks the PHY when the link is idle and wakes it "
                   + "when a packet arrives. Waking takes microseconds to milliseconds, and on "
                   + "several Realtek chipsets it is a known cause of link drops.",
            // Every vendor spells this differently and a machine usually has one of them.
            // Naming all of them costs nothing: absent keywords are skipped, never created.
            keywords:
            [
                "*EEE",
                "EnableGreenEthernet",
                "AdvancedEEE",
                "EEELinkAdvertisement",
                "EnableSavePowerNow",
                "PowerSavingMode",
            ],
            target: "0",
            absentReason: "no network adapter on this machine exposes an energy-efficient "
                        + "ethernet or green ethernet setting",
            tags: ["network", "adapter", "latency", "power", "eee"]);

        yield return new NetworkAdapterTweak(
            id: "network.flow-control-off",
            title: "Stop the network card sending pause frames",
            summary: "802.3x flow control lets the card tell the switch to stop transmitting for "
                   + "a while when its buffer fills. It pauses the whole link, not the one "
                   + "connection that caused it, so a large download can stall a game's packets.",
            keywords: ["*FlowControl"],
            target: "0",
            absentReason: "no network adapter on this machine exposes a flow control setting",
            tags: ["network", "adapter", "latency", "flow-control"]);
    }

    /// <summary>
    /// The services this tool is willing to move off Automatic start.
    ///
    /// The bar for being on this list is that somebody can plausibly not need the service and
    /// that the docs page can say what stops working -- not that turning it off has been shown
    /// to buy frames. Most of these are rated <see cref="Evidence.Plausible"/> for exactly that
    /// reason, and the pages say so. An entry that is honest about being unproven is more useful
    /// than one that has been left out, because leaving it out does not stop anyone doing it; it
    /// only stops them doing it somewhere that keeps a receipt.
    ///
    /// What is <b>not</b> here is anything on <see cref="WindowsServices.Protected"/>: audio,
    /// boot, sign-in, networking and the security stack. Those are refused at the SCM layer, and
    /// naming one here throws while the catalog is being built rather than on a user's machine.
    ///
    /// Every entry defaults to Manual rather than Disabled. Manual means a wrong guess on this
    /// list costs a slower first start; Disabled means it costs a failure somewhere else with no
    /// mention of the service or of this program.
    /// </summary>
    private static IEnumerable<ITweak> ServiceTweaks()
    {
        // ---------------------------------------------------------------- indexing and caching

        yield return new WindowsServiceTweak(
            id: "services.search-indexer",
            serviceName: "WSearch",
            title: "Stop the Windows Search indexer starting at boot",
            summary: "Windows Search rebuilds its content index in the background, which is real "
                   + "disk and CPU work that arrives without warning. Start menu search keeps "
                   + "working; results for file contents get slower.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "indexing", "disk"]);

        yield return new WindowsServiceTweak(
            id: "services.sysmain",
            serviceName: "SysMain",
            title: "Stop SysMain (Superfetch) starting at boot",
            summary: "SysMain preloads what it predicts you will run next. On a hard disk that "
                   + "was a real win; on an SSD the prediction is worth much less than it costs, "
                   + "though how much less is not something anyone has measured for games.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "prefetch", "memory", "widely-shared"]);

        // ---------------------------------------------------------------- telemetry and reporting

        yield return new WindowsServiceTweak(
            id: "services.telemetry",
            serviceName: "DiagTrack",
            title: "Stop the telemetry collector starting at boot",
            summary: "Connected User Experiences and Telemetry batches diagnostic data and "
                   + "uploads it. The work is small and mostly idle-time; the claim that it "
                   + "costs frames is not one this repo can support.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "telemetry", "widely-shared"]);

        yield return new WindowsServiceTweak(
            id: "services.error-reporting",
            serviceName: "WerSvc",
            title: "Stop Windows Error Reporting starting at boot",
            summary: "The service behind the 'checking for a solution' dialog. When a game "
                   + "crashes it collects a dump and uploads it, which is disk and network work "
                   + "at the exact moment you are trying to get back into the match.",
            category: TweakCategories.Interruptions,
            evidence: Evidence.Plausible,
            tags: ["service", "telemetry", "crash"]);

        yield return new WindowsServiceTweak(
            id: "services.diagnostic-policy",
            serviceName: "DPS",
            title: "Stop the Diagnostic Policy Service starting at boot",
            summary: "Runs the troubleshooters and the 'diagnose network problems' machinery. "
                   + "Idle almost all of the time; when it does run, it runs scripts.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "diagnostics", "widely-shared"]);

        yield return new WindowsServiceTweak(
            id: "services.program-compatibility",
            serviceName: "PcaSvc",
            title: "Stop the Program Compatibility Assistant starting at boot",
            summary: "Watches what you launch for known-bad patterns and offers to apply "
                   + "compatibility shims. The dialog it produces takes focus, which is how it "
                   + "ends up here rather than under performance.",
            category: TweakCategories.Interruptions,
            evidence: Evidence.Plausible,
            tags: ["service", "compatibility", "focus"]);

        // ---------------------------------------------------------------- network and bandwidth

        yield return new WindowsServiceTweak(
            id: "services.delivery-optimization",
            serviceName: "DoSvc",
            title: "Stop the update peer-to-peer service starting at boot",
            summary: "Delivery Optimization is the process that actually uploads update chunks "
                   + "to other machines. Saturated upstream is one of the few things on this "
                   + "list with a direct, obvious effect on ping.",
            category: TweakCategories.Ping,
            evidence: Evidence.Plausible,
            tags: ["service", "bandwidth", "windows-update", "p2p"]);

        yield return new WindowsServiceTweak(
            id: "services.remote-registry",
            serviceName: "RemoteRegistry",
            title: "Stop the Remote Registry service starting at boot",
            summary: "Lets another machine on the network read and write this one's registry. "
                   + "Already Disabled on a default Windows 11 install; this pins it so nothing "
                   + "quietly turns it back on.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "network", "attack-surface"]);

        // ---------------------------------------------------------------- hardware you may not have

        yield return new WindowsServiceTweak(
            id: "services.print-spooler",
            serviceName: "Spooler",
            title: "Stop the Print Spooler starting at boot",
            summary: "Runs whether or not a printer has ever been attached. The honest reason to "
                   + "turn it off is that it has a long history of remote code execution bugs, "
                   + "not that it costs frames - it sits idle.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "printing", "attack-surface", "widely-shared"]);

        yield return new WindowsServiceTweak(
            id: "services.fax",
            serviceName: "Fax",
            title: "Stop the Fax service starting at boot",
            summary: "Sends and receives faxes through a modem. If that sentence describes "
                   + "nothing attached to your PC, this service has nothing to do.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "printing", "legacy"]);

        yield return new WindowsServiceTweak(
            id: "services.bluetooth",
            serviceName: "bthserv",
            title: "Stop the Bluetooth Support Service starting at boot",
            summary: "Discovers and pairs Bluetooth devices. Turn this off only on a machine "
                   + "with no Bluetooth mouse, headset or controller - the failure mode is a "
                   + "peripheral that silently stops pairing.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "bluetooth", "peripherals"]);

        yield return new WindowsServiceTweak(
            id: "services.touch-keyboard",
            serviceName: "TabletInputService",
            title: "Stop the touch keyboard and handwriting panel starting at boot",
            summary: "Draws the on-screen keyboard and the handwriting panel. On a desktop with "
                   + "no touchscreen and no pen it has nothing to draw, and it is one of the "
                   + "things that can pop a panel over a fullscreen game.",
            category: TweakCategories.Interruptions,
            evidence: Evidence.Plausible,
            tags: ["service", "touch", "overlay"]);

        yield return new WindowsServiceTweak(
            id: "services.payments-nfc",
            serviceName: "SEMgrSvc",
            title: "Stop the payments and NFC service starting at boot",
            summary: "Manages the secure element used for tap-to-pay. Desktop PCs do not have "
                   + "one, so on a desktop this is a service with no hardware to manage.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "nfc", "mobile"]);

        yield return new WindowsServiceTweak(
            id: "services.geolocation",
            serviceName: "lfsvc",
            title: "Stop the Geolocation service starting at boot",
            summary: "Works out where the machine is for apps that ask, and prompts you when "
                   + "they do. A desktop with no GPS gets its answer from Wi-Fi and IP, which is "
                   + "vague enough to be worth little to you and something to somebody else.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "location", "privacy"]);

        // ---------------------------------------------------------------- features you may not use

        yield return new WindowsServiceTweak(
            id: "services.downloaded-maps",
            serviceName: "MapsBroker",
            title: "Stop the Downloaded Maps Manager starting at boot",
            summary: "Downloads and updates offline maps for the Maps app in the background. "
                   + "Real disk and network work, on a schedule you did not choose, for a "
                   + "feature most people have never opened.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "maps", "disk", "bandwidth"]);

        yield return new WindowsServiceTweak(
            id: "services.retail-demo",
            serviceName: "RetailDemo",
            title: "Stop the Retail Demo service starting at boot",
            summary: "Drives the demo loop on machines sitting on a shop shelf. There is no "
                   + "case for it on a PC somebody owns.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "retail"]);

        yield return new WindowsServiceTweak(
            id: "services.windows-insider",
            serviceName: "wisvc",
            title: "Stop the Windows Insider service starting at boot",
            summary: "Enrols the machine in preview builds and checks in about them. Does "
                   + "nothing at all unless you have joined the Insider Program.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "windows-update", "insider"]);

        // ---------------------------------------------------------------- the Xbox stack
        //
        // Previously refused outright. That was the wrong call: plenty of people play only on
        // Steam, with a mouse or a DualSense, and for them this is four Automatic services that
        // do nothing. What it is NOT is free -- the docs pages say plainly which one breaks the
        // controller, and all four default to Manual so a wrong guess repairs itself on demand.

        yield return new WindowsServiceTweak(
            id: "services.xbox-accessory",
            serviceName: "XboxGipSvc",
            title: "Stop Xbox Accessory Management starting at boot",
            summary: "This is the service that makes an Xbox controller work. Turn it off only "
                   + "if you do not use one - it is the single most common way tools like this "
                   + "break the thing they were run for.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "xbox", "controller"]);

        yield return new WindowsServiceTweak(
            id: "services.xbox-live-auth",
            serviceName: "XblAuthManager",
            title: "Stop Xbox Live Auth Manager starting at boot",
            summary: "Signs you in to Xbox Live. Game Pass and Microsoft Store games will not "
                   + "launch without it; a Steam-only library never calls it.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "xbox", "game-pass"]);

        yield return new WindowsServiceTweak(
            id: "services.xbox-game-save",
            serviceName: "XblGameSave",
            title: "Stop Xbox Live Game Save starting at boot",
            summary: "Syncs cloud saves for Xbox and Store titles. The failure mode if you do "
                   + "use those is a save that quietly stops syncing, which is worse than one "
                   + "that fails loudly.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "xbox", "cloud-saves"]);

        yield return new WindowsServiceTweak(
            id: "services.xbox-networking",
            serviceName: "XboxNetApiSvc",
            title: "Stop Xbox Live Networking starting at boot",
            summary: "Handles NAT traversal and multiplayer connectivity for Microsoft Store "
                   + "titles. Nothing outside that ecosystem uses it.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "xbox", "multiplayer"]);

        // ---------------------------------------------------------------- things that pop up

        yield return new WindowsServiceTweak(
            id: "services.push-notifications",
            serviceName: "WpnService",
            title: "Stop the push notification platform starting at boot",
            summary: "Receives notifications from Microsoft's servers and hands them to the "
                   + "shell to draw. This is the pipe the toasts arrive through, as opposed to "
                   + "the setting that decides whether they get shown.",
            category: TweakCategories.Interruptions,
            evidence: Evidence.Plausible,
            tags: ["service", "notifications", "toast", "focus"]);

        yield return new WindowsServiceTweak(
            id: "services.printer-notifications",
            serviceName: "PrintNotify",
            title: "Stop Printer Extensions and Notifications starting at boot",
            summary: "Draws the printer's own dialogs: out of paper, low toner, job finished. "
                   + "Separate from the spooler, and the half that actually puts something on "
                   + "your screen.",
            category: TweakCategories.Interruptions,
            evidence: Evidence.Plausible,
            tags: ["service", "printing", "notifications"]);

        // ------------------------------------------------------- more hardware you may not have

        yield return new WindowsServiceTweak(
            id: "services.smart-card",
            serviceName: "SCardSvr",
            title: "Stop the Smart Card service starting at boot",
            summary: "Talks to smart card readers, the kind used for corporate badge sign-in. "
                   + "Consumer machines do not have one; machines that do are usually managed, "
                   + "and this is not a tool for those.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "smartcard", "enterprise"]);

        yield return new WindowsServiceTweak(
            id: "services.sensors",
            serviceName: "SensorService",
            title: "Stop the sensor service starting at boot",
            summary: "Manages ambient light, accelerometer and screen-rotation sensors. A "
                   + "desktop tower has none of them, so this is a service waiting on hardware "
                   + "that was never fitted.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "sensors", "laptop"]);

        yield return new WindowsServiceTweak(
            id: "services.phone",
            serviceName: "PhoneSvc",
            title: "Stop the telephony state service starting at boot",
            summary: "Manages the device's own telephony state - cellular calls on a Windows "
                   + "tablet with a modem. Not Phone Link, which is a separate app and keeps "
                   + "working.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "phone", "mobile"]);

        // -------------------------------------------------------- more features you may not use

        yield return new WindowsServiceTweak(
            id: "services.wallet",
            serviceName: "WalletService",
            title: "Stop the Wallet service starting at boot",
            summary: "Backs the Microsoft Wallet app, which stored cards and passes. The app "
                   + "shipped, was discontinued, and the service stayed.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "wallet", "legacy"]);

        yield return new WindowsServiceTweak(
            id: "services.parental-controls",
            serviceName: "WpcMonSvc",
            title: "Stop the Parental Controls service starting at boot",
            summary: "Enforces Microsoft Family Safety: screen time limits, content filters, "
                   + "activity reports. Does nothing unless a family group covers this machine "
                   + "- and if one does, turning this off is the whole point of it.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "family-safety", "parental-controls"]);

        yield return new WindowsServiceTweak(
            id: "services.offline-files",
            serviceName: "CscService",
            title: "Stop the Offline Files service starting at boot",
            summary: "Caches network shares so they stay readable when the network is not. A "
                   + "domain feature; on a home machine with no redirected folders it has "
                   + "nothing to cache.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "network", "enterprise", "disk"]);

        yield return new WindowsServiceTweak(
            id: "services.windows-backup",
            serviceName: "SDRSVC",
            title: "Stop the Windows Backup service starting at boot",
            summary: "Runs the old Backup and Restore (Windows 7) engine. Real disk and CPU "
                   + "work when it fires, and it fires on a schedule you set once and forgot - "
                   + "or never set, in which case it does nothing at all.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "backup", "disk"]);

        yield return new WindowsServiceTweak(
            id: "services.wmp-network-sharing",
            serviceName: "WMPNetworkSvc",
            title: "Stop Windows Media Player network sharing starting at boot",
            summary: "Advertises your media library to DLNA devices on the LAN and streams to "
                   + "them. Windows Media Player itself is gone from a default Windows 11; the "
                   + "sharing service is often still registered.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "media", "dlna", "network"]);

        // ---------------------------------------------------------------- network odds and ends

        yield return new WindowsServiceTweak(
            id: "services.internet-sharing",
            serviceName: "SharedAccess",
            title: "Stop Internet Connection Sharing starting at boot",
            summary: "Turns this PC into a router for other devices, and is what backs Mobile "
                   + "Hotspot. If you have never shared this machine's connection, it has "
                   + "nothing to share.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "network", "hotspot", "attack-surface"]);

        yield return new WindowsServiceTweak(
            id: "services.netbios-helper",
            serviceName: "lmhosts",
            title: "Stop the TCP/IP NetBIOS Helper starting at boot",
            summary: "Resolves names over NetBIOS, a protocol older than most of the people "
                   + "turning it off. Still used by some file sharing and by anything that "
                   + "browses the old Network Neighborhood.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "network", "netbios", "legacy"]);

        yield return new WindowsServiceTweak(
            id: "services.link-tracking",
            serviceName: "TrkWks",
            title: "Stop Distributed Link Tracking starting at boot",
            summary: "Keeps shortcuts working when their target file is moved or renamed, by "
                   + "recording NTFS object IDs. Cheap, but it is real per-file bookkeeping for "
                   + "a feature few people notice either way.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "ntfs", "shortcuts"]);

        yield return new WindowsServiceTweak(
            id: "services.alljoyn-router",
            serviceName: "AJRouter",
            title: "Stop the AllJoyn Router service starting at boot",
            summary: "Routes messages for AllJoyn, an Internet-of-Things protocol Microsoft "
                   + "backed and then stopped backing. Almost nothing speaks it, and nothing "
                   + "you play does.",
            category: TweakCategories.Background,
            evidence: Evidence.Plausible,
            tags: ["service", "iot", "legacy", "widely-shared"]);
    }

    public static TweakRegistry CreateRegistry() => new(CreateAll());
}
