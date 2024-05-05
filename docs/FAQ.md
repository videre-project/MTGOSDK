# Frequently Asked Questions

## What kind of applications can be built with this SDK?

This SDK can be used to build applications that interact cooperatively with the MTGO client, such as collection trackers, game analysis tools, or other assistive tools. This includes informational tools for streamers, integrations for importing/exporting decklists, etc.

Other projects like the [Videre Tracker](https://github.com/videre-project/Tracker) may allow extensions to be built that interact with it and the MTGO client by extending this SDK. This SDK should be preferred only in cases where an extension requires additional APIs not offered by the Tracker.

This SDK does not intend nor support creating cheating tools or hacks that violate the [End User License Agreement (EULA)](https://www.mtgo.com/en/mtgo/eula) of MTGO. Creation of such tools may result in account termination and other legal consequences, including prosecution by Daybreak Games or the Videre Project under applicable law.

## Does using this SDK modify the MTGO client?

No, this SDK does not modify or alter the MTGO client's code or start-up behavior.

Interactions with the MTGO process are managed through use of the [Microsoft.Diagnostics.Runtime (ClrMD)](https://github.com/microsoft/clrmd) library from Microsoft, which is injected into the MTGO process to inspect client memory through the use of snapshots. As this library does not support inspecting it's own process, it is isolated into a separate [application domain](https://learn.microsoft.com/en-us/dotnet/framework/app-domains/application-domains) which acts as a process boundary. This allows for the SDK to read the state of the MTGO client without the ability to modify it's memory.

State changes are managed by [reflection](https://learn.microsoft.com/en-us/dotnet/framework/reflection-and-codedom/reflection) on public APIs and events bound to MTGO's UI (i.e. viewmodels and controllers). This avoids hooking or executing code from MTGO's UI thread which might stall or break the state of the application. Instead, the underlying events bound to a UI element are used to propagate an interaction without locking the UI thread. These events enforce the same limits and safeguards set for a UI interaction, ensuring that all interactions are handled correctly.

## Is this SDK safe to use?

As this project uses the [Microsoft.Diagnostics.Runtime (ClrMD)](https://github.com/microsoft/clrmd) library to inspect the MTGO client's memory, only [user-mode COM debugging APIs](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/getting-started-with-windbg) are used to inspect the runtime and inform reflection. These APIs along with .NET's security model restrict access to sensitive objects in memory (such as user credentials, e.g. `SecureString` objects), ensuring that the SDK is only used for legitimate purposes even if compromised.

This is enforced through a tightly-integrated type marshalling protocol that only allows public interfaces to be exposed by MTGO. The use of these types (from **MTGOSDK.Ref**) eagerly validates and caches type metadata for all interactions (including reflection) managed by the .NET runtime, which cannot be unloaded or modified for the lifetime of the application. This ensures that faults in the SDK cannot bypass or affect internal states of the client.

Additionally, the high-level APIs provided by this SDK helps ensure safety and security in writing applications by providing simplified abstractions for interacting with the MTGO client. By only utilizing public interfaces and events, applications built on top of the SDK are less likely to break between updates or introduce bugs or security vulnerabilities. This SDK is designed to be easy to use and understand, and provides a consistent API for building all shapes and sizes of applications for MTGO.

## Is this SDK legal to use?

Yes, however there are important restrictions on how this SDK can be used with the MTGO client.

Daybreak Games reserves the right to terminate your account without notice or liability (at its sole discretion) upon breach of MTGO's [End User License Agreement (EULA)](https://www.mtgo.com/en/mtgo/eula), the Daybreak Games [Terms of Service](https://www.daybreakgames.com/terms-of-service), or infringement of intellectual property rights (refer to **Section 16** of the EULA for more information on termination).

However, this restriction does not apply for purposes that do not violate the EULA and applicable law (such as copyright or intellectual property laws), or the aforementioned Daybreak Games policies. This includes such purposes protected under **Section 103** of the Digital Millennium Copyright Act (DMCA) ([17 USC § 1201 (f)](http://www.law.cornell.edu/uscode/text/17/1201)) that do not otherwise infringe upon the rights granted to Daybreak Games (refer to the [Disclaimer](/README#disclaimer) for more information on protections afforded to this project).

**This is not legal advice.** Please consult with a legal professional for your specific situation.
