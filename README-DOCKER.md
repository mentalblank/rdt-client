# [mentalblank/rdt-client](https://github.com/mentalblank/rdt-client)

This Dockerfile follows the [LinuxServer.io](https://linuxserver.io) pattern and utilizes the [s6-overlay](https://github.com/just-containers/s6-overlay) to run the application as a service within the container. This setup allows for initialization and permission-setting scripts to run before the application starts, ensuring proper configuration.

RDT-Client is a web interface to manage your torrents across Real-Debrid, AllDebrid, Premiumize, TorBox, and Debrid-Link.

## Features
- Add new torrents via magnet links or files.
- Download all files from a supported Debrid provider automatically.
- Auto-unpack files after download.
- Fake qBittorrent API to integrate easily with Sonarr, Radarr, CouchPotato, etc.
- Run as a background service on Windows or Linux.
- Built with Angular 15 and .NET 9.

## Supported Architectures

This image supports multiple architectures, including `x86-64`, `arm64`, and `armhf`. We leverage the Docker manifest for multi-platform compatibility, ensuring the appropriate image is pulled for your architecture.

For more information on multi-platform manifests, refer to [Docker Manifest Specification](https://github.com/docker/distribution/blob/master/docs/spec/manifest-v2-2.md#manifest-list) and the [announcement post](https://blog.linuxserver.io/2019/02/21/the-lsio-pipeline-project/).

### Supported Architectures and Tags:

| Architecture | Tag               |
|--------------|-------------------|
| x86-64       | `amd64-latest`     |
| arm64        | `arm64v8-latest`   |
| armhf        | `arm32v7-latest`   |

You can pull the correct image for your architecture by using `mentalblank/rdt-client`. Alternatively, specify a tag to pull a specific architecture image.

## Version Tags

We provide multiple version tags for different release stages. The `latest` tag points to the most recent stable release, while other tags represent under-development versions, which should be used with caution.

| Tag     | Description      |
|---------|------------------|
| latest  | Stable releases  |

## Usage

Below are example snippets to help you get started with creating a container for `rdt-client`.

### Docker-Compose ([recommended](https://docs.linuxserver.io/general/docker-compose))

Compatible with Docker-Compose v2 schemas.

```yaml
version: "3"
services:
  rdtclient:
    restart: unless-stopped
    container_name: rdtclient
    image: mentalblank/rdt-client:latest
    hostname: rdtclient
    environment:
      - PUID=1000          # Set the user ID for permissions
      - PGID=1000          # Set the group ID for permissions
      - TZ=ETC/UTC         # Set the timezone
    logging:
       driver: json-file
       options:
          max-size: 10m    # Limit log file size
    ports:
      - 6500/tcp           # Map port 6500 for the web interface
    networks:
      - saltbox
    labels:
      com.github.saltbox.saltbox_managed: true                                                                                 # Set true if using saltbox
      traefik.enable: true 
      traefik.http.routers.rdtclient-http.entrypoints: web 
      traefik.http.routers.rdtclient-http.middlewares: globalHeaders@file,redirect-to-https@docker,cloudflarewarp@docker 
      traefik.http.routers.rdtclient-http.rule: Host(`rdtclient.yourdomain.com`)                                               # Edit to include your domain
      traefik.http.routers.rdtclient-http.service: rdtclient 
      traefik.http.routers.rdtclient.entrypoints: websecure 
      traefik.http.routers.rdtclient.middlewares: globalHeaders@file,secureHeaders@file,cloudflarewarp@docker 
      traefik.http.routers.rdtclient.rule: Host(`rdtclient.yourdomain.com`)                                                    # Edit to include your domain
      traefik.http.routers.rdtclient.service: rdtclient 
      traefik.http.routers.rdtclient.tls.certresolver: cfdns 
      traefik.http.routers.rdtclient.tls.options: securetls@file 
      traefik.http.services.rdtclient.loadbalancer.server.port: 6500
    volumes:
      - /opt/rdtclient:/CONFIG                                                                                                # Edit to set config path
      - /etc/localtime:/etc/localtime:ro
      - /mnt:/mnt                                                                                                             # Edit to set mount path
      - /opt/rdtclient/data:/data                                                                                             # Edit to set data path
      - /opt/rdtclient/data/db:/data/db                                                                                       # Edit to set db path

networks:
  saltbox:                                                                                                                    # If using saltbox connect to network
    external: true
```

### Docker CLI

```
docker run -d \
  --name=rdtclient \
  -e PUID=1000 \
  -e PGID=1000 \
  -e TZ=Europe/London \
  -p 6500:6500 \
  -v <path to data>:/data/db \
  -v <path/to/downloads>:/data/downloads \
  --restart unless-stopped \
  mentalblank/rdtclient
```

## Parameters

Container images are configured using parameters passed at runtime. These parameters follow the format `<external>:<internal>`, where the external represents the host's path or value, and the internal refers to the path or setting within the container. For example, `-p 8080:80` would map port `80` inside the container to port `8080` on the host.

### Commonly Used Parameters

| Parameter                  | Function                                                                 |
|----------------------------|--------------------------------------------------------------------------|
| `-p 6500:6500`              | Exposes the WebUI on port `6500` to be accessible from the host.         |
| `-e PUID=1000`              | Sets the user ID (UID) for the container. This is useful for permission management when accessing shared volumes. |
| `-e PGID=1000`              | Sets the group ID (GID) for the container. Similarly, this helps with file permissions, especially for shared volumes. |
| `-e TZ=Europe/London`       | Specifies the timezone for the container. Example values: `Europe/London`, `America/New_York`, etc. |
| `-v /data/db:/config`       | Mounts the host’s `/data/db` directory to the container's `/config` path. This is where the application stores its persistent data (e.g., database). |
| `-v /data/downloads:/downloads` | Mounts the host’s `/data/downloads` directory to the container’s `/downloads` path. This is where torrents or files will be downloaded. |

### Additional Parameters

You can customize the container further by passing additional environment variables and volume mappings. Some examples include:

- **Customizing the Base Path for WebUI**:  
  To change the default base URL path for the WebUI (e.g., `http://myserver.com/rdt`), you can set the `BASE_PATH` environment variable:

  ```bash
  -e BASE_PATH=/rdt

## Environment Variables from Files (Docker Secrets)

You can securely set environment variables in your container by referencing files with a special `FILE__` prefix. This allows sensitive information, such as passwords or API keys, to be securely passed to the container without hardcoding them in the environment.

**Example:** If you have a secret stored in a file at `/run/secrets/mysecretpassword`, you can set the environment variable `PASSWORD` from the contents of that file with the following command:

```bash
-e FILE__PASSWORD=/run/secrets/mysecretpassword
```

## Umask for Running Applications

For all of our images, we provide the ability to override the default **umask** settings for services started within the containers using the optional `-e UMASK=022` setting.  
Keep in mind, **umask** is not the same as **chmod**; it subtracts from permissions based on its value—it does not add.  
Please read more about it [here](https://en.wikipedia.org/wiki/Umask) before asking for support.

## User / Group Identifiers

When using volumes (`-v` flags), permissions issues can arise between the host OS and the container. To avoid this, we allow you to specify the user `PUID` and group `PGID`.

Ensure any volume directories on the host are owned by the same user you specify, and any permissions issues will vanish like magic.

For example, if `PUID=1000` and `PGID=1000`, to find your `PUID` and `PGID`, use the command:

```
  $ id username
    uid=1000(dockeruser) gid=1000(dockergroup) groups=1000(dockergroup)
```

## Application Setup

The WebUI can be accessed at:  
`http://<your-ip>:6500`

## Support Info

- Shell access whilst the container is running:
  - `docker exec -it rtdclient /bin/bash`
- **To monitor the logs** of the container in real-time:
  - `docker logs -f rdtclient`
- **Container version number**:
  - `docker inspect -f '{{ index .Config.Labels "build_version" }}' rdtclient`
- **Image version number**:
  - `docker inspect -f '{{ index .Config.Labels "build_version" }}' mentalblank/rdtclient`

## Updating Info

Most of our images are static, versioned, and require an image update and container recreation to update the app inside. Please consult the [Application Setup](#application-setup) section above to see if it is recommended for the image.

Below are the instructions for updating containers:

### Via Docker Compose
- **Update all images**:
  - `docker-compose pull`
  - **Or update a single image**:
    - `docker-compose pull rdtclient`
- **Let compose update all containers as necessary**:
  - `docker-compose up -d`
    - **Or update a single container**:
      - `docker-compose up -d rdtclient`
- **Remove the old dangling images**:
  - `docker image prune` 

### Via Docker Run
- **Update the image**:
  - `docker pull mentalblank/rdtclient`
- **Stop the running container**:
  - `docker stop rdtclient`
- **Delete the container**:
  - `docker rm rdtclient`
- **Recreate a new container** with the same docker run parameters as instructed above (if mapped correctly to a host folder, your `/data` folder and settings will be preserved).
- **Remove the old dangling images**:
  - `docker image prune`

### Via Watchtower auto-updater (only use if you don't remember the original parameters)
- **Pull the latest image at its tag and replace it with the same env variables in one run**:
  ```bash
  docker run --rm \
  -v /var/run/docker.sock:/var/run/docker.sock \
  containrrr/watchtower \
  --run-once rtdclient
  ```
- **Remove the old dangling images**:
  - `docker image prune`

> **Note:** We do not endorse the use of Watchtower as a solution to automated updates of existing Docker containers. We generally discourage automated updates. However, this is a useful tool for one-time manual updates of containers where you have forgotten the original parameters. In the long term, we highly recommend using [Docker Compose](https://docs.linuxserver.io/general/docker-compose).

### Image Update Notifications - Diun (Docker Image Update Notifier)
* We recommend [Diun](https://crazymax.dev/diun/) for update notifications. Other tools that automatically update containers unattended are not recommended or supported.

## Building locally

If you want to make local modifications to these images for development purposes or just to customize the logic:

```bash
git clone https://github.com/ravensorb/docker-rdtclient.git
cd docker-rdtclient
docker build \
  --no-cache \
  --pull \
  -t mentalblank/rdt-client:latest .
```

The ARM variants can be built on x86_64 hardware using `multiarch/qemu-user-static`
```
docker run --rm --privileged multiarch/qemu-user-static:register --reset
```

Once registered, you can define the dockerfile to use with `-f Dockerfile.aarch64`.
