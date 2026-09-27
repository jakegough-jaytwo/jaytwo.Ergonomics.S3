# classic community image that still bundles `mc` for `mc ready local`.
FROM docker.io/pgsty/minio:RELEASE.2026-08-04T00-00-00Z
ENV MINIO_ROOT_USER=minioadmin
ENV MINIO_ROOT_PASSWORD=minioadmin
CMD ["server", "/data", "--console-address", ":9001"]
