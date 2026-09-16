FROM ubuntu:20.04 AS dotnet-sdk

ENV DEBIAN_FRONTEND=noninteractive
ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH="${PATH}:/usr/share/dotnet:/root/.dotnet/tools"

RUN apt-get update \
  && apt-get install -y --no-install-recommends \
    ca-certificates \
    curl \
    libicu-dev \
    make \
    openssl \
  && apt-get clean \
  && apt-get autoremove \
  && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh \
  && chmod +x /tmp/dotnet-install.sh \
  && /tmp/dotnet-install.sh --channel 8.0 --install-dir "$DOTNET_ROOT" \
  && /tmp/dotnet-install.sh --channel 6.0 --runtime dotnet --install-dir "$DOTNET_ROOT" \
  && /tmp/dotnet-install.sh --channel 3.1 --runtime dotnet --install-dir "$DOTNET_ROOT" \
  && ln -sf "$DOTNET_ROOT/dotnet" /usr/bin/dotnet \
  && rm /tmp/dotnet-install.sh

FROM dotnet-sdk AS base

FROM base AS builder
WORKDIR /build
COPY . /build
RUN make deps restore build
