"""Prepare public, pinned inputs for the optional native installation/update tests."""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import urllib.parse
import urllib.request


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, type=Path, help="New or empty output directory")
    parser.add_argument("--loader", help="Explicit Fabric loader version; defaults to latest stable")
    parser.add_argument("--direct", action="store_true", help="Ignore proxy environment for these public downloads")
    args = parser.parse_args()
    root = args.output.resolve()
    if root.exists() and (not root.is_dir() or any(root.iterdir())):
        parser.error("Output must be a new or empty directory; existing files are never removed")
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({})) if args.direct else urllib.request.build_opener()

    def fetch(url, limit=16 * 1024 * 1024):
        request = urllib.request.Request(url, headers={"User-Agent": "PCL-for-CAUMC-development-validation/0.1"})
        with opener.open(request, timeout=30) as response:
            data = response.read(limit + 1)
        if len(data) > limit:
            raise ValueError("Integration fixture exceeds size limit")
        return data

    def get_json(url):
        return json.loads(fetch(url))

    minecraft = "1.21.1"  # Same target as the native regression assertions.
    manifest = get_json("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json")
    version_entry = next(item for item in manifest["versions"] if item["id"] == minecraft)
    version_bytes = fetch(version_entry["url"])
    if hashlib.sha1(version_bytes).hexdigest() != version_entry["sha1"]:
        raise ValueError("Official Minecraft version metadata SHA1 mismatch")
    vanilla = json.loads(version_bytes)
    loaders = get_json(f"https://meta.fabricmc.net/v2/versions/loader/{minecraft}")
    loader_entry = next(item for item in loaders if item["loader"]["version"] == args.loader) if args.loader else next(item for item in loaders if item["loader"]["stable"])
    loader = loader_entry["loader"]["version"]
    profile = get_json(f"https://meta.fabricmc.net/v2/versions/loader/{minecraft}/{urllib.parse.quote(loader, safe='')}/profile/json")

    query = urllib.parse.urlencode({"game_versions": json.dumps([minecraft]), "loaders": json.dumps(["fabric"])})
    versions = get_json("https://api.modrinth.com/v2/project/fabric-api/version?" + query)
    releases = sorted((item for item in versions if item["version_type"] == "release"), key=lambda item: item["date_published"])
    if len(releases) < 2:
        raise ValueError("At least two compatible releases are required to verify a real update")
    old, latest = releases[0], releases[-1]
    old_file = next(item for item in old["files"] if item["primary"])
    latest_file = next(item for item in latest["files"] if item["primary"])
    name = old_file["filename"]
    if Path(name).name != name or "/" in name or "\\" in name or not name.endswith(".jar"):
        raise ValueError("Unexpected remote fixture filename")
    jar = fetch(old_file["url"])
    if hashlib.sha1(jar).hexdigest() != old_file["hashes"]["sha1"]:
        raise ValueError("Official Modrinth sample SHA1 mismatch")

    # This profile supplies real version/loader context for update compatibility checks.
    # It is not an installed game; the separate Fabric test exercises the actual installer.
    context = copy.deepcopy(vanilla)
    context.update({"id": "caumc-mod-update-regression", "clientVersion": minecraft, "mainClass": profile["mainClass"]})
    context["libraries"].extend(profile["libraries"])
    for key, values in profile.get("arguments", {}).items():
        context.setdefault("arguments", {}).setdefault(key, []).extend(values)

    root.mkdir(parents=True, exist_ok=True)
    mods = root / "mod-update"
    mods.mkdir()

    def write_json(path, value):
        path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    write_json(root / "fabric-install-request.json", {"MinecraftVersion": minecraft, "LoaderVersion": loader, "MinecraftJson": version_entry["url"], "ClientSha1": vanilla["downloads"]["client"]["sha1"]})
    write_json(root / "fabric-official-metadata.json", loader_entry)
    (root / "minecraft-version.json").write_bytes(version_bytes)
    write_json(mods / "game-profile.json", context)
    write_json(mods / "old-version.json", old)
    write_json(mods / "latest-version.json", latest)
    write_json(mods / "request.json", {"minecraft": minecraft, "oldFile": name, "oldSHA1": old_file["hashes"]["sha1"], "latestFile": latest_file["filename"], "latestSHA1": latest_file["hashes"]["sha1"], "oldVersion": old["version_number"], "latestVersion": latest["version_number"]})
    (mods / name).write_bytes(jar)
    print(f"Prepared Minecraft {minecraft}, Fabric {loader}, Mod update {old['version_number']} -> {latest['version_number']}")
    print(root)


if __name__ == "__main__":
    main()
