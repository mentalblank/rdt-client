# ⚠️ Test Branch Notice

## This branch is for testing purposes only.

It will not be merged into the master branch or origin repository. A better solution exists and will be implemented separately and properly.
`docker pull mentalblank/rdt-client:2.0.112` if you do want to try it out.

***TorBox Usenet Support Test Fork***
- Enable the simultaneous handling of Torrent and NZB downloads through the TorBox provider.
- NZB files can be uploaded, added via URLs using the magnet link input box on the “Add New Torrent” page, or through blackhole
- Fake SABnzbd API endpoints and settings for enabling basic compatibility with *arr apps.
- Changes to Authentication and creation of a Client API Key for use with the fake SABnzbd endpoints

***Notes:***
- NZB uploads are currently assigned filename as the default name, This updates from TorBox if cached and can lead to `example\example.mkv` or `nzb\nzb.mkv`
- Optional passwords for NZB archives cannot be configured.
- Only the default post-processing configuration used by TorBox is applied.
- User interface remains unchanged.
- NZBs require a higher buffer / chunks / timeout setting to not overwhelm the TorBox API.
- Full testing still required.