FROM openjdk:19-jdk-alpine
WORKDIR /opt

ENV GATLING_VERSION 3.9.5

RUN mkdir -p gatling

RUN apk add --update wget curl bash openssl libc6-compat && \
  mkdir -p /tmp/downloads && \
  wget -q -O /tmp/downloads/gatling-$GATLING_VERSION.zip \
  https://repo1.maven.org/maven2/io/gatling/highcharts/gatling-charts-highcharts-bundle/$GATLING_VERSION/gatling-charts-highcharts-bundle-$GATLING_VERSION-bundle.zip && \
  mkdir -p /tmp/archive && cd /tmp/archive && \
  unzip /tmp/downloads/gatling-$GATLING_VERSION.zip && \
  mv /tmp/archive/gatling-charts-highcharts-bundle-$GATLING_VERSION/* /opt/gatling/ && \
  rm -rf /tmp/*

ENV GATLING_HOME /opt/gatling

RUN mkdir -p stress-test
WORKDIR /opt/stress-test