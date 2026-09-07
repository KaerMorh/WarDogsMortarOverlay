FROM golang:1.27.1-alpine AS build
WORKDIR /src
COPY go.mod go.sum ./
RUN go mod download
COPY . .
RUN CGO_ENABLED=0 go build -trimpath -ldflags="-s -w" -o /wardogs-server ./cmd/wardogs-server

FROM scratch
COPY --from=build /wardogs-server /wardogs-server
USER 65532:65532
ENV WARDOGS_ADDR=0.0.0.0:8080 GOMEMLIMIT=192MiB
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s --retries=3 CMD ["/wardogs-server", "healthcheck"]
ENTRYPOINT ["/wardogs-server"]
