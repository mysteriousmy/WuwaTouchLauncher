# .NET SDK 10.0.402 on Alpine; use musl for a fully static C runtime.
FROM mcr.microsoft.com/dotnet/sdk@sha256:230c3fbae0ceedc70bca04eed2bfb5694e8b248cadbb81010f41b7a993601f55
RUN apk add --no-cache clang build-base cmake zlib-dev zlib-static \
    openssl-dev openssl-libs-static sqlite-dev sqlite-static
ARG BUILD_UID=1000
ARG BUILD_GID=1000
RUN addgroup -g ${BUILD_GID} -S builder && adduser -D -u ${BUILD_UID} -G builder builder
WORKDIR /work
